using System.Text.RegularExpressions;
using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Npgsql;

namespace FieldService.Api.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed partial class TableConstraintTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string Check = PostgresErrorCodes.CheckViolation;

    private const string Unique = PostgresErrorCodes.UniqueViolation;

    private const string InsertToken =
        "INSERT INTO fieldservice.refresh_tokens (id, session_id, token_hash, created_at, consumed_at, replaced_by_id) VALUES (@new_id, @session_id, ";

    private const string InsertHistory =
        "INSERT INTO fieldservice.job_history (id, job_id, action, from_status, to_status, actor_user_id, technician_id, note, occurred_at) VALUES (@new_id, @in_progress_job_id, ";

    private const string FixtureSession =
        "INSERT INTO fieldservice.user_sessions (id, user_id, client, created_at, last_refreshed_at, idle_expires_at, absolute_expires_at, revoked_at, revoked_reason) VALUES (@session_id, @technician_id, 'Mobile', @at - interval '10 minutes', NULL, @at - interval '10 minutes' + interval '7 days', @at - interval '10 minutes' + interval '30 days', NULL, NULL);";

    private const string FixtureTokens =
        "INSERT INTO fieldservice.refresh_tokens (id, session_id, token_hash, created_at, consumed_at, replaced_by_id) VALUES (@token_id, @session_id, sha256('fixture token'::bytea), @at - interval '10 minutes', NULL, NULL), (@successor_id, @session_id, sha256('fixture successor'::bytea), @at, NULL, NULL);";

    private const string ChecklistSnapshot =
        "SELECT concat_ws('|', (SELECT string_agg(t::text, ',' ORDER BY t.id) FROM fieldservice.checklist_templates t), (SELECT string_agg(i::text, ',' ORDER BY i.id) FROM fieldservice.checklist_items i), (SELECT string_agg(f::text, ',' ORDER BY f.id) FROM fieldservice.checklist_reading_fields f))";

    private const string CheckConstraintsOutsideInspections =
        "SELECT c.conname FROM pg_constraint c JOIN pg_class r ON r.oid = c.conrelid JOIN pg_namespace n ON n.oid = r.relnamespace WHERE n.nspname = 'fieldservice' AND c.contype = 'c' AND r.relname NOT LIKE 'inspection%'";

    private const string UniqueIndexesOutsideKeysAndInspections =
        "SELECT i.relname FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid JOIN pg_class r ON r.oid = x.indrelid JOIN pg_namespace n ON n.oid = r.relnamespace WHERE n.nspname = 'fieldservice' AND x.indisunique AND NOT EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conindid = x.indexrelid) AND r.relname NOT LIKE 'inspection%'";

    private static readonly Guid _hvacTemplateId = ChecklistTemplateSeed.TemplateId(2);

    private static readonly Dictionary<string, (string Sql, string SqlState, string Constraint)> _appRejected = new(StringComparer.Ordinal)
    {
        ["C28 users: display_name empty"] = ("UPDATE fieldservice.users SET display_name = '' WHERE id = @administrator_id", Check, "ck_users_display_name_not_blank"),
        ["C29 users: display_name of three spaces"] = ("UPDATE fieldservice.users SET display_name = '   ' WHERE id = @administrator_id", Check, "ck_users_display_name_not_blank"),
        ["C31 users: email without @"] = ("UPDATE fieldservice.users SET email = 'dana.whitfield.example' WHERE id = @administrator_id", Check, "ck_users_email_format"),
        ["C32 users: email with nothing before @"] = ("UPDATE fieldservice.users SET email = '@fieldservice.example' WHERE id = @administrator_id", Check, "ck_users_email_format"),
        ["C33 users: email with nothing after @"] = ("UPDATE fieldservice.users SET email = 'dana@' WHERE id = @administrator_id", Check, "ck_users_email_format"),
        ["C35 users: email with an upper-case letter"] = ("UPDATE fieldservice.users SET email = 'Dana@fieldservice.example' WHERE id = @administrator_id", Check, "ck_users_email_lower_case"),
        ["C36 users: role Dispatcher, the label the UI shows"] = ("UPDATE fieldservice.users SET role = 'Dispatcher' WHERE id = @administrator_id", Check, "ck_users_role"),
        ["C37 users: role in lower case"] = ("UPDATE fieldservice.users SET role = 'technician' WHERE id = @technician_id", Check, "ck_users_role"),
        ["C38 users: updated_at 1 ms before created_at"] = ("UPDATE fieldservice.users SET updated_at = created_at - interval '1 millisecond' WHERE id = @administrator_id", Check, "ck_users_updated_at"),
        ["C40 technicians: phone_number empty"] = ("UPDATE fieldservice.technicians SET phone_number = '' WHERE user_id = @technician_id", Check, "ck_technicians_phone_number_not_blank"),
        ["C41 technicians: phone_number of two spaces"] = ("UPDATE fieldservice.technicians SET phone_number = '  ' WHERE user_id = @technician_id", Check, "ck_technicians_phone_number_not_blank"),
        ["C42 technicians: trade HVAC, the label the UI shows"] = ("UPDATE fieldservice.technicians SET trade = 'HVAC' WHERE user_id = @technician_id", Check, "ck_technicians_trade"),
        ["C43 technicians: trade outside the set"] = ("UPDATE fieldservice.technicians SET trade = 'Carpentry' WHERE user_id = @technician_id", Check, "ck_technicians_trade"),
        ["C44 user_sessions: client outside the set"] = ("UPDATE fieldservice.user_sessions SET client = 'Tablet' WHERE id = @session_id", Check, "ck_user_sessions_client"),
        ["C45 user_sessions: client in lower case"] = ("UPDATE fieldservice.user_sessions SET client = 'web' WHERE id = @session_id", Check, "ck_user_sessions_client"),
        ["C46 user_sessions: idle expiry equal to created_at"] = ("UPDATE fieldservice.user_sessions SET idle_expires_at = created_at WHERE id = @session_id", Check, "ck_user_sessions_expiry"),
        ["C47 user_sessions: absolute expiry equal to created_at"] = ("UPDATE fieldservice.user_sessions SET idle_expires_at = created_at + interval '1 hour', absolute_expires_at = created_at WHERE id = @session_id", Check, "ck_user_sessions_expiry"),
        ["C48 user_sessions: idle expiry 1 ms past the absolute expiry"] = ("UPDATE fieldservice.user_sessions SET idle_expires_at = absolute_expires_at + interval '1 millisecond' WHERE id = @session_id", Check, "ck_user_sessions_expiry"),
        ["C50 user_sessions: last refresh 1 ms before created_at"] = ("UPDATE fieldservice.user_sessions SET last_refreshed_at = created_at - interval '1 millisecond' WHERE id = @session_id", Check, "ck_user_sessions_last_refreshed_at"),
        ["C52 user_sessions: revoked_at without a reason"] = ("UPDATE fieldservice.user_sessions SET revoked_at = @at WHERE id = @session_id", Check, "ck_user_sessions_revocation"),
        ["C53 user_sessions: a reason without revoked_at"] = ("UPDATE fieldservice.user_sessions SET revoked_reason = 'SignOut' WHERE id = @session_id", Check, "ck_user_sessions_revocation"),
        ["C54 user_sessions: revoked with a reason outside the set"] = ("UPDATE fieldservice.user_sessions SET revoked_at = @at, revoked_reason = 'Expired' WHERE id = @session_id", Check, "ck_user_sessions_revoked_reason"),
        ["C56 refresh_tokens: token hash of 31 bytes"] = (InsertToken + "decode(repeat('ab', 31), 'hex'), @at, NULL, NULL)", Check, "ck_refresh_tokens_token_hash_length"),
        ["C57 refresh_tokens: token hash of 33 bytes"] = (InsertToken + "decode(repeat('ab', 33), 'hex'), @at, NULL, NULL)", Check, "ck_refresh_tokens_token_hash_length"),
        ["C58 refresh_tokens: empty token hash"] = (InsertToken + "''::bytea, @at, NULL, NULL)", Check, "ck_refresh_tokens_token_hash_length"),
        ["C60 refresh_tokens: live token with a successor"] = ("UPDATE fieldservice.refresh_tokens SET replaced_by_id = @successor_id WHERE id = @token_id", Check, "ck_refresh_tokens_replacement"),
        ["C61 refresh_tokens: token replaced by itself"] = ("UPDATE fieldservice.refresh_tokens SET consumed_at = @at, replaced_by_id = id WHERE id = @token_id", Check, "ck_refresh_tokens_replacement"),
        ["C71 jobs: Assigned job without assigned_at"] = ("UPDATE fieldservice.jobs SET assigned_at = NULL WHERE id = @assigned_job_id", Check, "ck_jobs_assigned_at"),
        ["C72 jobs: Unassigned job with assigned_at"] = ("UPDATE fieldservice.jobs SET assigned_at = updated_at WHERE id = @unassigned_job_id", Check, "ck_jobs_assigned_at"),
        ["C73 jobs: Unassigned with a technician"] = ("UPDATE fieldservice.jobs SET status = 'Unassigned' WHERE id = @assigned_job_id", Check, "ck_jobs_assignee"),
        ["C74 jobs: Assigned without a technician"] = ("UPDATE fieldservice.jobs SET status = 'Assigned' WHERE id = @unassigned_job_id", Check, "ck_jobs_assignee"),
        ["C75 jobs: InProgress without a technician"] = ("UPDATE fieldservice.jobs SET assigned_technician_id = NULL, assigned_at = NULL WHERE id = @in_progress_job_id", Check, "ck_jobs_assignee"),
        ["C76 jobs: OnHold without a technician"] = ("UPDATE fieldservice.jobs SET status = 'OnHold', assigned_technician_id = NULL, assigned_at = NULL WHERE id = @in_progress_job_id", Check, "ck_jobs_assignee"),
        ["C77 jobs: Completed without a technician"] = ("UPDATE fieldservice.jobs SET assigned_technician_id = NULL, assigned_at = NULL WHERE id = @completed_job_id", Check, "ck_jobs_assignee"),
        ["C79 jobs: Cancelled without cancelled_at"] = ("UPDATE fieldservice.jobs SET status = 'Cancelled' WHERE id = @assigned_job_id", Check, "ck_jobs_cancelled_at"),
        ["C80 jobs: cancelled_at on an Assigned job"] = ("UPDATE fieldservice.jobs SET cancelled_at = updated_at WHERE id = @assigned_job_id", Check, "ck_jobs_cancelled_at"),
        ["C81 jobs: Completed without completed_at"] = ("UPDATE fieldservice.jobs SET status = 'Completed' WHERE id = @in_progress_job_id", Check, "ck_jobs_completed_at"),
        ["C82 jobs: completed_at on an InProgress job"] = ("UPDATE fieldservice.jobs SET completed_at = updated_at WHERE id = @in_progress_job_id", Check, "ck_jobs_completed_at"),
        ["C83 jobs: CancelJob's UPDATE on a Completed job"] = ("UPDATE fieldservice.jobs SET cancelled_at = @at, status = 'Cancelled', updated_at = @at WHERE id = @completed_job_id", Check, "ck_jobs_completed_at"),
        ["C84 jobs: site_access_notes empty"] = ("UPDATE fieldservice.jobs SET site_access_notes = '' WHERE id = @unassigned_job_id", Check, "ck_jobs_optional_text_not_blank"),
        ["C85 jobs: contact_name of three spaces"] = ("UPDATE fieldservice.jobs SET contact_name = '   ' WHERE id = @unassigned_job_id", Check, "ck_jobs_optional_text_not_blank"),
        ["C86 jobs: contact_phone empty"] = ("UPDATE fieldservice.jobs SET contact_phone = '' WHERE id = @unassigned_job_id", Check, "ck_jobs_optional_text_not_blank"),
        ["C87 jobs: equipment of one space"] = ("UPDATE fieldservice.jobs SET equipment = ' ' WHERE id = @unassigned_job_id", Check, "ck_jobs_optional_text_not_blank"),
        ["C89 jobs: priority outside the set"] = ("UPDATE fieldservice.jobs SET priority = 'Critical' WHERE id = @unassigned_job_id", Check, "ck_jobs_priority"),
        ["C90 jobs: priority in lower case"] = ("UPDATE fieldservice.jobs SET priority = 'high' WHERE id = @unassigned_job_id", Check, "ck_jobs_priority"),
        ["C91 jobs: title empty"] = ("UPDATE fieldservice.jobs SET title = '' WHERE id = @unassigned_job_id", Check, "ck_jobs_required_text_not_blank"),
        ["C92 jobs: description of three spaces"] = ("UPDATE fieldservice.jobs SET description = '   ' WHERE id = @unassigned_job_id", Check, "ck_jobs_required_text_not_blank"),
        ["C93 jobs: customer_name empty"] = ("UPDATE fieldservice.jobs SET customer_name = '' WHERE id = @unassigned_job_id", Check, "ck_jobs_required_text_not_blank"),
        ["C94 jobs: site_address of one space"] = ("UPDATE fieldservice.jobs SET site_address = ' ' WHERE id = @unassigned_job_id", Check, "ck_jobs_required_text_not_blank"),
        ["C95 jobs: scheduled_end equal to scheduled_start"] = ("UPDATE fieldservice.jobs SET scheduled_end = scheduled_start WHERE id = @unassigned_job_id", Check, "ck_jobs_schedule"),
        ["C96 jobs: scheduled_end before scheduled_start"] = ("UPDATE fieldservice.jobs SET scheduled_end = scheduled_start - interval '1 minute' WHERE id = @unassigned_job_id", Check, "ck_jobs_schedule"),
        ["C98 jobs: InProgress without started_at"] = ("UPDATE fieldservice.jobs SET started_at = NULL WHERE id = @in_progress_job_id", Check, "ck_jobs_started_at"),
        ["C99 jobs: Completed without started_at"] = ("UPDATE fieldservice.jobs SET started_at = NULL WHERE id = @completed_job_id", Check, "ck_jobs_started_at"),
        ["C100 jobs: Assigned with started_at"] = ("UPDATE fieldservice.jobs SET started_at = updated_at WHERE id = @assigned_job_id", Check, "ck_jobs_started_at"),
        ["C101 jobs: Unassigned with started_at"] = ("UPDATE fieldservice.jobs SET started_at = updated_at WHERE id = @unassigned_job_id", Check, "ck_jobs_started_at"),
        ["C103 jobs: status outside the set"] = ("UPDATE fieldservice.jobs SET status = 'Open' WHERE id = @unassigned_job_id", Check, "ck_jobs_status"),
        ["C104 jobs: status as the UI shows it"] = ("UPDATE fieldservice.jobs SET status = 'In progress' WHERE id = @unassigned_job_id", Check, "ck_jobs_status"),
        ["C27 jobs: job completed 1 ms before it started: the clock stepped back between start and submission"] = ("UPDATE fieldservice.jobs SET status = 'Completed', completed_at = started_at - interval '1 millisecond', updated_at = @at WHERE id = @in_progress_job_id", Check, "ck_jobs_timestamps"),
        ["C105 jobs: a command's clock 1 ms behind the job's creation"] = ("UPDATE fieldservice.jobs SET updated_at = created_at - interval '1 millisecond' WHERE id = @assigned_job_id", Check, "ck_jobs_timestamps"),
        ["C108 job_history: action outside the set"] = (InsertHistory + "'Reopened', 'InProgress', 'InProgress', @technician_id, NULL, NULL, @at)", Check, "ck_job_history_action"),
        ["C109 job_history: action in lower case"] = (InsertHistory + "'started', 'Assigned', 'InProgress', @technician_id, NULL, NULL, @at)", Check, "ck_job_history_action"),
        ["C110 job_history: from_status outside the set"] = (InsertHistory + "'Started', 'Open', 'InProgress', @technician_id, NULL, NULL, @at)", Check, "ck_job_history_from_status"),
        ["C111 job_history: Created with a from_status"] = (InsertHistory + "'Created', 'Unassigned', 'Unassigned', @administrator_id, NULL, NULL, @at)", Check, "ck_job_history_from_status_presence"),
        ["C112 job_history: Started without a from_status"] = (InsertHistory + "'Started', NULL, 'InProgress', @technician_id, NULL, NULL, @at)", Check, "ck_job_history_from_status_presence"),
        ["C113 job_history: PutOnHold without a note"] = (InsertHistory + "'PutOnHold', 'InProgress', 'OnHold', @technician_id, NULL, NULL, @at)", Check, "ck_job_history_note"),
        ["C114 job_history: PutOnHold with a note of three spaces"] = (InsertHistory + "'PutOnHold', 'InProgress', 'OnHold', @technician_id, NULL, '   ', @at)", Check, "ck_job_history_note"),
        ["C115 job_history: Cancelled with an empty note"] = (InsertHistory + "'Cancelled', 'InProgress', 'Cancelled', @administrator_id, NULL, '', @at)", Check, "ck_job_history_note"),
        ["C116 job_history: Started with a note"] = (InsertHistory + "'Started', 'Assigned', 'InProgress', @technician_id, NULL, 'On my way', @at)", Check, "ck_job_history_note"),
        ["C117 job_history: Assigned without a technician"] = (InsertHistory + "'Assigned', 'Unassigned', 'Assigned', @administrator_id, NULL, NULL, @at)", Check, "ck_job_history_technician"),
        ["C118 job_history: Started with a technician"] = (InsertHistory + "'Started', 'Assigned', 'InProgress', @technician_id, @technician_id, NULL, @at)", Check, "ck_job_history_technician"),
        ["C119 job_history: Started to OnHold"] = (InsertHistory + "'Started', 'Assigned', 'OnHold', @technician_id, NULL, NULL, @at)", Check, "ck_job_history_to_status"),
        ["C120 job_history: Created to Assigned"] = (InsertHistory + "'Created', NULL, 'Assigned', @administrator_id, NULL, NULL, @at)", Check, "ck_job_history_to_status"),
        ["C121 job_history: Cancelled to Unassigned"] = (InsertHistory + "'Cancelled', 'Assigned', 'Unassigned', @administrator_id, NULL, 'Customer cancelled', @at)", Check, "ck_job_history_to_status"),
        ["U04 users: two users with one email"] = ("UPDATE fieldservice.users SET email = (SELECT email FROM fieldservice.users WHERE id = @administrator_id) WHERE id = @technician_id", Unique, "ix_users_email"),
        ["U05 refresh_tokens: two tokens with one hash"] = (InsertToken + "sha256('fixture token'::bytea), @at, NULL, NULL)", Unique, "ix_refresh_tokens_token_hash"),
        ["U06 jobs: two jobs with one number"] = ("UPDATE fieldservice.jobs SET number = (SELECT number FROM fieldservice.jobs WHERE id = @unassigned_job_id) WHERE id = @assigned_job_id", Unique, "ix_jobs_number"),
        ["U07 technicians: a second technician row for one user"] = ("INSERT INTO fieldservice.technicians (user_id, trade, phone_number, is_on_shift) VALUES (@technician_id, 'Hvac', '(555) 010-0000', true)", Unique, "pk_technicians"),
    };

    private static readonly Dictionary<string, string> _appAccepted = new(StringComparer.Ordinal)
    {
        ["C30 users: display_name of one tab (btrim removes spaces only)"] = "UPDATE fieldservice.users SET display_name = E'\\t' WHERE id = @administrator_id",
        ["C34 users: email with one character on each side of @"] = "UPDATE fieldservice.users SET email = 'd@f' WHERE id = @administrator_id",
        ["C39 users: updated_at equal to created_at"] = "UPDATE fieldservice.users SET updated_at = created_at WHERE id = @administrator_id",
        ["C49 user_sessions: idle expiry capped at the absolute expiry"] = "UPDATE fieldservice.user_sessions SET idle_expires_at = absolute_expires_at WHERE id = @session_id",
        ["C51 user_sessions: last refresh equal to created_at"] = "UPDATE fieldservice.user_sessions SET last_refreshed_at = created_at WHERE id = @session_id",
        ["C55 user_sessions: revoked by an operator"] = "UPDATE fieldservice.user_sessions SET revoked_at = @at, revoked_reason = 'Operator' WHERE id = @session_id",
        ["C59 refresh_tokens: token hash of 32 bytes"] = InsertToken + "decode(repeat('ab', 32), 'hex'), @at, NULL, NULL)",
        ["C62 refresh_tokens: rotation: consumed with a successor"] = "UPDATE fieldservice.refresh_tokens SET consumed_at = @at, replaced_by_id = @successor_id WHERE id = @token_id",
        ["C63 refresh_tokens: grace-retry revocation: consumed without a successor"] = "UPDATE fieldservice.refresh_tokens SET consumed_at = @at WHERE id = @token_id",
        ["C78 jobs: Cancelled without a technician (either is allowed)"] = "UPDATE fieldservice.jobs SET assigned_technician_id = NULL, assigned_at = NULL WHERE id = @cancelled_job_id",
        ["C88 jobs: all four optional texts NULL"] = "UPDATE fieldservice.jobs SET site_access_notes = NULL, contact_name = NULL, contact_phone = NULL, equipment = NULL WHERE id = @unassigned_job_id",
        ["C97 jobs: scheduled_end 1 ms after scheduled_start"] = "UPDATE fieldservice.jobs SET scheduled_end = scheduled_start + interval '1 millisecond' WHERE id = @unassigned_job_id",
        ["C102 jobs: Cancelled with started_at (cancelled while in progress)"] = "UPDATE fieldservice.jobs SET started_at = updated_at WHERE id = @cancelled_job_id",
        ["C106 jobs: job completed at the instant it started"] = "UPDATE fieldservice.jobs SET status = 'Completed', completed_at = started_at, updated_at = @at WHERE id = @in_progress_job_id",
        ["C107 jobs: updated_at equal to created_at"] = "UPDATE fieldservice.jobs SET updated_at = created_at WHERE id = @assigned_job_id",
        ["C122 job_history: a Created entry: from_status NULL, no technician, no note"] = InsertHistory + "'Created', NULL, 'Unassigned', @administrator_id, NULL, NULL, @at)",
        ["C123 job_history: an Assigned entry with its technician"] = InsertHistory + "'Assigned', 'Unassigned', 'Assigned', @administrator_id, @technician_id, NULL, @at)",
        ["C124 job_history: a PutOnHold entry with a note"] = InsertHistory + "'PutOnHold', 'InProgress', 'OnHold', @technician_id, NULL, 'Waiting for a part', @at)",
        ["C125 job_history: a Resumed entry"] = InsertHistory + "'Resumed', 'OnHold', 'InProgress', @technician_id, NULL, NULL, @at)",
    };

    private static readonly Dictionary<string, (string Sql, string SqlState, string Constraint)> _migratorRejected = new(StringComparer.Ordinal)
    {
        ["C64 checklist_templates: trade outside the set"] = ("UPDATE fieldservice.checklist_templates SET trade = 'Roofing' WHERE id = @hvac_template_id", Check, "ck_checklist_templates_trade"),
        ["C65 checklist_items: position 0"] = ("UPDATE fieldservice.checklist_items SET position = 0 WHERE id = @item1", Check, "ck_checklist_items_position"),
        ["C66 checklist_items: position 100"] = ("UPDATE fieldservice.checklist_items SET position = 100 WHERE id = @item10", Check, "ck_checklist_items_position"),
        ["C68 checklist_reading_fields: position 0"] = ("UPDATE fieldservice.checklist_reading_fields SET position = 0 WHERE id = @suction", Check, "ck_checklist_reading_fields_position"),
        ["C69 checklist_reading_fields: position 10"] = ("UPDATE fieldservice.checklist_reading_fields SET position = 10 WHERE id = @discharge", Check, "ck_checklist_reading_fields_position"),
        ["U08 checklist_templates: two templates with one name"] = ("UPDATE fieldservice.checklist_templates SET name = 'Boiler service' WHERE id = @hvac_template_id", Unique, "ix_checklist_templates_name"),
        ["U09 checklist_items: two items of a template at one position"] = ("UPDATE fieldservice.checklist_items SET position = 1 WHERE id = @item2", Unique, "ix_checklist_items_template_id_position"),
        ["U10 checklist_reading_fields: two fields of an item at one position"] = ("UPDATE fieldservice.checklist_reading_fields SET position = 1 WHERE id = @discharge", Unique, "ix_checklist_reading_fields_checklist_item_id_position"),
    };

    private static readonly Dictionary<string, string> _migratorAccepted = new(StringComparer.Ordinal)
    {
        ["C67 checklist_items: position 99"] = "UPDATE fieldservice.checklist_items SET position = 99 WHERE id = @item10",
        ["C70 checklist_reading_fields: position 9"] = "UPDATE fieldservice.checklist_reading_fields SET position = 9 WHERE id = @discharge",
    };

    public static TheoryData<string> AppRejectedProbes => [.. _appRejected.Keys];

    public static TheoryData<string> AppAcceptedProbes => [.. _appAccepted.Keys];

    public static TheoryData<string> MigratorRejectedProbes => [.. _migratorRejected.Keys];

    public static TheoryData<string> MigratorAcceptedProbes => [.. _migratorAccepted.Keys];

    [Theory]
    [MemberData(nameof(AppRejectedProbes))]
    public async Task AStatementThatBreaksATableRuleFailsWithItsSqlStateAndConstraint(string probe)
    {
        var (sql, sqlState, constraint) = _appRejected[probe];
        var rows = await ArrangeAsync();
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = Command(sql, connection, transaction, rows);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(sqlState, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(AppAcceptedProbes))]
    public async Task AStatementAtTheEdgeOfATableRuleSucceedsInARolledBackTransaction(string probe)
    {
        var rows = await ArrangeAsync();
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = Command(_appAccepted[probe], connection, transaction, rows);

        var affected = await command.ExecuteNonQueryAsync(CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    [Theory]
    [MemberData(nameof(MigratorRejectedProbes))]
    public async Task AChecklistChangeThatBreaksARuleFailsForTheMigratorWithItsSqlStateAndConstraint(string probe)
    {
        var (sql, sqlState, constraint) = _migratorRejected[probe];
        var rows = await ArrangeAsync();
        var before = await ChecklistSnapshotAsync();

        var exception = await RunAsMigratorWithoutCommitAsync(
            sql,
            rows,
            command => Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken)));

        Assert.Equal(sqlState, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
        Assert.Equal(before, await ChecklistSnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(MigratorAcceptedProbes))]
    public async Task AChecklistChangeAtTheEdgeOfARuleSucceedsForTheMigratorAndIsNeverCommitted(string probe)
    {
        var rows = await ArrangeAsync();
        var before = await ChecklistSnapshotAsync();

        var affected = await RunAsMigratorWithoutCommitAsync(
            _migratorAccepted[probe],
            rows,
            command => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(1, affected);
        Assert.Equal(before, await ChecklistSnapshotAsync());
    }

    [Fact]
    public async Task EveryCheckConstraintOutsideTheInspectionTablesIsBrokenByAProbe()
    {
        var declared = await CatalogNamesAsync(CheckConstraintsOutsideInspections);

        var probed = ProbedConstraints(Check);

        Assert.Equal(declared, probed);
    }

    [Fact]
    public async Task EveryUniqueIndexThatBacksNoKeyOutsideTheInspectionTablesIsBrokenByAProbe()
    {
        var declared = await CatalogNamesAsync(UniqueIndexesOutsideKeysAndInspections);

        var probed = ProbedConstraints(Unique);

        Assert.NotEmpty(declared);
        Assert.Empty(declared.Except(probed, StringComparer.Ordinal));
    }

    private static List<string> ProbedConstraints(string sqlState) =>
    [
        .. _appRejected.Values.Concat(_migratorRejected.Values)
            .Where(probe => probe.SqlState == sqlState)
            .Select(probe => probe.Constraint)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private async Task<List<string>> CatalogNamesAsync(string sql)
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }

    private async Task<T> RunAsMigratorWithoutCommitAsync<T>(string sql, Rows rows, Func<NpgsqlCommand, Task<T>> run)
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = Command(sql, connection, transaction, rows);
        var result = await run(command);
        await transaction.RollbackAsync(CancellationToken);
        return result;
    }

    private async Task<string?> ChecklistSnapshotAsync()
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(ChecklistSnapshot, connection);
        return (string?)await command.ExecuteScalarAsync(CancellationToken);
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction? transaction, Rows rows)
    {
        var referenced = ParameterPattern().Matches(sql)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in rows.Parameters)
        {
            if (referenced.Contains(name))
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        return command;
    }

    private async Task<Rows> ArrangeAsync()
    {
        var now = UtcInstant.Truncate(Fixture.Factory.Clock.GetUtcNow());
        var data = new TestData(Fixture, now.AddMinutes(-30));
        var administrator = await data.CreateAdministratorAsync(CancellationToken);
        var technician = await data.CreateTechnicianAsync(Trade.Hvac, CancellationToken);
        var inProgressJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        var completedJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        var unassignedJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        var assignedJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        var cancelledJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        inProgressJob = await new TestData(Fixture, now.AddMinutes(-25)).MoveToAsync(
            inProgressJob,
            technician,
            JobStatus.InProgress,
            CancellationToken);
        var draftTime = new TestData(Fixture, now.AddMinutes(-20));
        await draftTime.SaveInspectionAsync(
            inProgressJob,
            [
                new ChecklistAnswer(Item(1), InspectionResult.Pass, null, []),
                new ChecklistAnswer(
                    Item(3),
                    InspectionResult.Pass,
                    null,
                    [new ChecklistReading(Field(3, 1), 118m), new ChecklistReading(Field(3, 2), 285m)]),
                new ChecklistAnswer(Item(4), InspectionResult.Pass, null, []),
                new ChecklistAnswer(Item(5), InspectionResult.Fail, "Bearing noise at start-up.", []),
            ],
            CancellationToken);
        await draftTime.CreateInspectionAsync(completedJob, technician, submitted: true, CancellationToken);
        var moveTime = new TestData(Fixture, now.AddMinutes(-15));
        await moveTime.MoveToAsync(assignedJob, technician, JobStatus.Assigned, CancellationToken);
        await moveTime.MoveToAsync(cancelledJob, technician, JobStatus.Cancelled, CancellationToken);
        var rows = new Rows(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["in_progress_job_id"] = inProgressJob.Id,
            ["unassigned_job_id"] = unassignedJob.Id,
            ["completed_job_id"] = completedJob.Id,
            ["assigned_job_id"] = assignedJob.Id,
            ["cancelled_job_id"] = cancelledJob.Id,
            ["session_id"] = Guid.CreateVersion7(),
            ["token_id"] = Guid.CreateVersion7(),
            ["successor_id"] = Guid.CreateVersion7(),
            ["item1"] = Item(1),
            ["item2"] = Item(2),
            ["item10"] = Item(10),
            ["suction"] = Field(3, 1),
            ["discharge"] = Field(3, 2),
            ["hvac_template_id"] = _hvacTemplateId,
            ["administrator_id"] = administrator.Id,
            ["technician_id"] = technician.UserId,
            ["new_id"] = Guid.CreateVersion7(),
            ["at"] = now,
        });
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await ExecuteAsync(FixtureSession, connection, rows);
        await ExecuteAsync(FixtureTokens, connection, rows);
        return rows;
    }

    private static async Task ExecuteAsync(string sql, NpgsqlConnection connection, Rows rows)
    {
        await using var command = Command(sql, connection, transaction: null, rows);
        Assert.True(await command.ExecuteNonQueryAsync(CancellationToken) > 0);
    }

    private static Guid Item(int position) => ChecklistTemplateSeed.ItemId(2, position);

    private static Guid Field(int item, int position) => ChecklistTemplateSeed.ReadingFieldId(2, item, position);

    [GeneratedRegex(@"@(?<name>[a-z0-9_]+)")]
    private static partial Regex ParameterPattern();

    private sealed record Rows(IReadOnlyDictionary<string, object> Parameters);
}
