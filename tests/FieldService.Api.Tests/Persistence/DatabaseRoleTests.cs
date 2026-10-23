using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Npgsql;

namespace FieldService.Api.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class DatabaseRoleTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string InspectionIdParameter = "inspection_id";

    private static readonly Guid _hvacTemplateId = ChecklistTemplateSeed.TemplateId(2);

    public static TheoryData<string> DeniedStatements =>
    [
        "UPDATE fieldservice.job_history SET id = id WHERE false",
        "DELETE FROM fieldservice.job_history WHERE false",
        "INSERT INTO fieldservice.checklist_templates DEFAULT VALUES",
        "UPDATE fieldservice.checklist_items SET id = id WHERE false",
        "DELETE FROM fieldservice.checklist_reading_fields WHERE false",
        "SELECT 1 FROM fieldservice.checklist_items FOR KEY SHARE",
        "DELETE FROM fieldservice.\"__EFMigrationsHistory\" WHERE false",
        "CREATE TABLE fieldservice.probe (id integer)",
        "CREATE TABLE public.probe (id integer)",
        "ALTER TABLE fieldservice.jobs ADD COLUMN probe integer",
        "SET ROLE fieldservice_migrator",
        "SELECT setval('fieldservice.jobs_number_seq', 1)",
        "SELECT nextval('fieldservice.jobs_number_seq')",
        "TRUNCATE fieldservice.job_history",
        "TRUNCATE fieldservice.inspections",
        "TRUNCATE fieldservice.inspection_answers",
        "TRUNCATE fieldservice.inspection_readings",
        "ALTER SEQUENCE fieldservice.jobs_number_seq RESTART",
        "ALTER TABLE fieldservice.jobs ALTER COLUMN number RESTART",
        "CREATE OR REPLACE FUNCTION fieldservice.advance_job_number() RETURNS bigint LANGUAGE sql AS 'SELECT 1::bigint'",
        "CREATE OR REPLACE FUNCTION fieldservice.refuse_submitted_inspection_change() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NEW; END'",
        "ALTER TABLE fieldservice.inspections DISABLE TRIGGER tr_inspections_submitted_lock",
        "ALTER TABLE fieldservice.inspection_answers DISABLE TRIGGER tr_inspection_answers_submitted_lock",
        "ALTER TABLE fieldservice.inspection_readings DISABLE TRIGGER tr_inspection_readings_submitted_lock",
        "DROP TRIGGER tr_inspections_submitted_lock ON fieldservice.inspections",
        "DROP TRIGGER tr_inspection_answers_submitted_lock ON fieldservice.inspection_answers",
        "DROP TRIGGER tr_inspection_readings_submitted_lock ON fieldservice.inspection_readings",
        "SET session_replication_role = replica",
    ];

    public static TheoryData<string> SubmittedInspectionChanges =>
    [
        "UPDATE fieldservice.inspections SET updated_at = updated_at WHERE id = @inspection_id",
        "DELETE FROM fieldservice.inspections WHERE id = @inspection_id",
        "UPDATE fieldservice.inspection_answers SET note = note WHERE inspection_id = @inspection_id",
        "DELETE FROM fieldservice.inspection_answers WHERE inspection_id = @inspection_id",
        "UPDATE fieldservice.inspection_readings SET value = value WHERE inspection_id = @inspection_id",
        "DELETE FROM fieldservice.inspection_readings WHERE inspection_id = @inspection_id",
        "UPDATE fieldservice.inspections SET submitted_at = NULL WHERE id = @inspection_id",
    ];

    public static TheoryData<string> DraftInspectionUpdates =>
    [
        "UPDATE fieldservice.inspections SET updated_at = updated_at WHERE id = @inspection_id",
        "UPDATE fieldservice.inspection_answers SET note = note WHERE inspection_id = @inspection_id",
        "UPDATE fieldservice.inspection_readings SET value = value WHERE inspection_id = @inspection_id",
        "UPDATE fieldservice.inspections SET submitted_at = NULL WHERE id = @inspection_id",
    ];

    [Theory]
    [MemberData(nameof(DeniedStatements))]
    public async Task StatementOutsideTheAppRolePrivilegesFailsWithInsufficientPrivilege(string statement)
    {
        var exception = await ExecuteExpectingFailureAsync(
            await Fixture.OpenAppConnectionAsync(CancellationToken),
            statement);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task AppRoleCanReadTheMigrationHistory()
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);

        var rows = await ScalarAsync<long>(connection, "SELECT count(*) FROM fieldservice.\"__EFMigrationsHistory\"");

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task AppRoleInsertsAJobNumberedByTheIdentityWithoutAnySequencePrivilege()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        var hasSequencePrivilege = await ScalarAsync<bool>(
            connection,
            "SELECT has_sequence_privilege('fieldservice.jobs_number_seq', 'USAGE') OR has_sequence_privilege('fieldservice.jobs_number_seq', 'SELECT') OR has_sequence_privilege('fieldservice.jobs_number_seq', 'UPDATE')");
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO fieldservice.jobs (id, title, description, priority, customer_name, site_address, scheduled_start, scheduled_end, checklist_template_id, status, created_by_user_id, created_at, updated_at)
            VALUES (@id, 'Identity probe', 'Identity probe', 'Normal', 'Identity probe', 'Identity probe', @start, @end, @template, 'Unassigned', @creator, @now, @now)
            RETURNING number
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7(data.Now));
        insert.Parameters.AddWithValue("start", data.Now.AddDays(1));
        insert.Parameters.AddWithValue("end", data.Now.AddDays(1).AddHours(1));
        insert.Parameters.AddWithValue("template", _hvacTemplateId);
        insert.Parameters.AddWithValue("creator", administrator.Id);
        insert.Parameters.AddWithValue("now", data.Now);

        var number = (long)(await insert.ExecuteScalarAsync(CancellationToken))!;
        await transaction.RollbackAsync(CancellationToken);

        Assert.False(hasSequencePrivilege);
        Assert.True(number >= 1001);
    }

    [Fact]
    public async Task AdvanceJobNumberMovesTheIdentityPastAnExplicitlyNumberedJob()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        var next = await ScalarAsync<long>(connection, "SELECT fieldservice.advance_job_number()");
        var explicitNumber = next + 10;
        await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken, explicitNumber);

        var advanced = await ScalarAsync<long>(connection, "SELECT fieldservice.advance_job_number()");
        var following = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);

        Assert.Equal(explicitNumber + 1, advanced);
        Assert.Equal(explicitNumber + 1, following.Number);
    }

    [Fact]
    public async Task AdvanceJobNumberLeavesAnIdentityThatIsAlreadyAheadUnchanged()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        var next = await ScalarAsync<long>(connection, "SELECT fieldservice.advance_job_number()");
        await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken, next - 1);

        var unchanged = await ScalarAsync<long>(connection, "SELECT fieldservice.advance_job_number()");
        var following = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);

        Assert.Equal(next, unchanged);
        Assert.Equal(next, following.Number);
    }

    [Fact]
    public async Task OnlyTheAppRoleMayExecuteAdvanceJobNumberAndPublicMayExecuteNeitherFunction()
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);

        var publicAdvance = await ScalarAsync<bool>(
            connection,
            "SELECT has_function_privilege('public', 'fieldservice.advance_job_number()', 'EXECUTE')");
        var publicTrigger = await ScalarAsync<bool>(
            connection,
            "SELECT has_function_privilege('public', 'fieldservice.refuse_submitted_inspection_change()', 'EXECUTE')");
        var appAdvance = await ScalarAsync<bool>(
            connection,
            "SELECT has_function_privilege('fieldservice_app', 'fieldservice.advance_job_number()', 'EXECUTE')");

        Assert.False(publicAdvance);
        Assert.False(publicTrigger);
        Assert.True(appAdvance);
    }

    [Fact]
    public async Task BothRolesHaveEveryRoleAttributeOffAndTheAppRoleIsLimitedToThirtyConnections()
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls, rolconnlimit FROM pg_roles WHERE rolname IN ('fieldservice_app', 'fieldservice_migrator') ORDER BY rolname",
            connection);
        var roles = new List<(string Name, bool Elevated, int ConnectionLimit)>();
        await using (var reader = await command.ExecuteReaderAsync(CancellationToken))
        {
            while (await reader.ReadAsync(CancellationToken))
            {
                var elevated = reader.GetBoolean(1) || reader.GetBoolean(2) || reader.GetBoolean(3)
                    || reader.GetBoolean(4) || reader.GetBoolean(5);
                roles.Add((reader.GetString(0), elevated, reader.GetInt32(6)));
            }
        }

        Assert.Equal(
            [(DatabaseFixture.AppRole, false, 30), (DatabaseFixture.MigratorRole, false, -1)],
            roles);
    }

    [Fact]
    public async Task AppRoleSessionsRunWithTheRoleTimeouts()
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);

        var statementTimeout = await ScalarAsync<string>(connection, "SHOW statement_timeout");
        var lockTimeout = await ScalarAsync<string>(connection, "SHOW lock_timeout");
        var idleTimeout = await ScalarAsync<string>(connection, "SHOW idle_in_transaction_session_timeout");
        var searchPath = await ScalarAsync<string>(connection, "SHOW search_path");

        Assert.Equal("15s", statementTimeout);
        Assert.Equal("5s", lockTimeout);
        Assert.Equal("30s", idleTimeout);
        Assert.Equal("fieldservice", searchPath);
    }

    [Fact]
    public async Task PublicCannotConnectToTheDatabase()
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);

        var publicConnect = await ScalarAsync<bool>(
            connection,
            "SELECT has_database_privilege('public', current_database(), 'CONNECT')");
        var publicTemporary = await ScalarAsync<bool>(
            connection,
            "SELECT has_database_privilege('public', current_database(), 'TEMPORARY')");

        Assert.False(publicConnect);
        Assert.False(publicTemporary);
    }

    [Theory]
    [MemberData(nameof(SubmittedInspectionChanges))]
    public async Task ChangingASubmittedInspectionFailsWithObjectNotInPrerequisiteState(string statement)
    {
        var (submitted, _) = await ArrangeInspectionsAsync();

        var exception = await ExecuteExpectingFailureAsync(
            await Fixture.OpenAppConnectionAsync(CancellationToken),
            statement,
            new NpgsqlParameter(InspectionIdParameter, submitted.Id));

        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, exception.SqlState);
    }

    [Theory]
    [MemberData(nameof(DraftInspectionUpdates))]
    public async Task UpdatingADraftInspectionSucceedsInARolledBackTransaction(string statement)
    {
        var (_, draft) = await ArrangeInspectionsAsync();
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(statement, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter(InspectionIdParameter, draft.Id));

        var rows = await command.ExecuteNonQueryAsync(CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        Assert.True(rows > 0);
    }

    [Fact]
    public async Task AddingAnAnswerToASubmittedInspectionStaysAllowed()
    {
        var (submitted, _) = await ArrangeInspectionsAsync();
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO fieldservice.inspection_answers (inspection_id, checklist_item_id, result, note, answered_at) VALUES (@inspection_id, @item_id, 'Pass', NULL, @answered_at)",
            connection,
            transaction);
        command.Parameters.AddWithValue(InspectionIdParameter, submitted.Id);
        command.Parameters.AddWithValue("item_id", ChecklistTemplateSeed.ItemId(1, 2));
        command.Parameters.AddWithValue("answered_at", submitted.SubmittedAt!.Value);

        var rows = await command.ExecuteNonQueryAsync(CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task MigratorIsExemptFromTheSubmittedInspectionLock()
    {
        var (submitted, _) = await ArrangeInspectionsAsync();
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM fieldservice.inspection_readings WHERE inspection_id = @inspection_id",
            connection,
            transaction);
        command.Parameters.AddWithValue(InspectionIdParameter, submitted.Id);

        var rows = await command.ExecuteNonQueryAsync(CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        Assert.True(rows > 0);
    }

    private async Task<(Inspection Submitted, Inspection Draft)> ArrangeInspectionsAsync()
    {
        var data = new TestData(Fixture, Fixture.Factory.Clock.GetUtcNow());
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var submittedJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        var draftJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        var submitted = await data.CreateInspectionAsync(submittedJob, technician, submitted: true, CancellationToken);
        var draft = await data.CreateInspectionAsync(draftJob, technician, submitted: false, CancellationToken);
        return (submitted, draft);
    }

    private static async Task<PostgresException> ExecuteExpectingFailureAsync(
        NpgsqlConnection connection,
        string statement,
        params NpgsqlParameter[] parameters)
    {
        await using (connection)
        {
            await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
            await using var command = new NpgsqlCommand(statement, connection, transaction);
            command.Parameters.AddRange(parameters);
            return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken));
        }
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string statement)
    {
        await using var command = new NpgsqlCommand(statement, connection);
        return (T)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
