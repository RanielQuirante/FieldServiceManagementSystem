using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldService.Api.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class MigrationTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string InitialSchema = "20261020094242_InitialSchema";

    private const string EfCoreProductVersion = "10.0.13";

    private const string IdPrefix = "01a15175-3c00-7000-8000-";

    private static readonly int[] _itemsPerTemplate = [8, 10, 8, 7, 8];

    private static readonly (int Template, int Item, int Field)[] _readingFields =
    [
        (1, 1, 1), (1, 4, 1), (1, 5, 1),
        (2, 3, 1), (2, 3, 2), (2, 8, 1),
        (3, 3, 1),
        (4, 6, 1),
        (5, 1, 1), (5, 1, 2), (5, 2, 1), (5, 3, 1),
    ];

    private static readonly string[] _migrationFiles =
    [
        "20261020094242_InitialSchema.cs",
        "20261020094242_InitialSchema.Designer.cs",
        "FieldServiceDbContextModelSnapshot.cs",
    ];

    [Fact]
    public async Task AppliedHistoryIsExactlyInitialSchema()
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        var history = await ReadRowsAsync(
            connection,
            "SELECT migration_id, product_version FROM fieldservice.\"__EFMigrationsHistory\" ORDER BY migration_id",
            reader => (reader.GetString(0), reader.GetString(1)));
        await using var context = Fixture.CreateAppDbContext();

        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken);
        var pending = await context.Database.GetPendingMigrationsAsync(CancellationToken);

        Assert.Equal([(InitialSchema, EfCoreProductVersion)], history);
        Assert.Equal([InitialSchema], context.Database.GetMigrations());
        Assert.Equal([InitialSchema], applied);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task AppRoleReadsTheReferenceRowsWithTheirFixedIds()
    {
        await using var context = Fixture.CreateAppDbContext();

        var templateIds = await context.ChecklistTemplates.Select(template => template.Id).ToListAsync(CancellationToken);
        var itemIds = await context.Set<ChecklistItem>().Select(item => item.Id).ToListAsync(CancellationToken);
        var fieldIds = await context.Set<ChecklistReadingField>().Select(field => field.Id).ToListAsync(CancellationToken);

        Assert.Equal(ExpectedTemplateIds().Order(), templateIds.Order());
        Assert.Equal(ExpectedItemIds().Order(), itemIds.Order());
        Assert.Equal(ExpectedReadingFieldIds().Order(), fieldIds.Order());
    }

    [Fact]
    public async Task ReferenceRowsMatchTheSeedRowForRow()
    {
        await using var context = Fixture.CreateAppDbContext();

        var templates = await context.ChecklistTemplates
            .Select(template => new { template.Id, template.Name, template.Trade })
            .ToListAsync(CancellationToken);
        var items = await context.Set<ChecklistItem>()
            .Select(item => new { item.Id, item.TemplateId, item.Position, item.Prompt })
            .ToListAsync(CancellationToken);
        var fields = await context.Set<ChecklistReadingField>()
            .Select(field => new { field.Id, field.ChecklistItemId, field.Position, field.Label, field.Unit })
            .ToListAsync(CancellationToken);

        Assert.Equal(
            ChecklistTemplateSeed.Templates.Select(template => (template.Id, template.Name, template.Trade)).Order(),
            templates.Select(template => (template.Id, template.Name, template.Trade)).Order());
        Assert.Equal(
            ChecklistTemplateSeed.Items.Select(item => (item.Id, item.TemplateId, item.Position, item.Prompt)).Order(),
            items.Select(item => (item.Id, item.TemplateId, item.Position, item.Prompt)).Order());
        Assert.Equal(
            ChecklistTemplateSeed.ReadingFields
                .Select(field => (field.Id, field.ChecklistItemId, field.Position, field.Label, field.Unit))
                .Order(),
            fields.Select(field => (field.Id, field.ChecklistItemId, field.Position, field.Label, field.Unit)).Order());
    }

    [Fact]
    public void MigrationFilesContainNoComments()
    {
        var files = Directory.GetFiles(RepositoryPaths.MigrationsDirectory, "*.cs")
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var commentLines = files
            .SelectMany(file => File.ReadLines(Path.Combine(RepositoryPaths.MigrationsDirectory, file))
                .Select((line, index) => (File: file, Line: index + 1, Text: line)))
            .Where(line => line.Text.Contains("//", StringComparison.Ordinal)
                || line.Text.Contains("/*", StringComparison.Ordinal))
            .Select(line => $"{line.File}:{line.Line}")
            .ToArray();

        Assert.Equal(_migrationFiles.Order(StringComparer.Ordinal), files);
        Assert.Empty(commentLines);
    }

    [Fact]
    public async Task SchemaHoldsTheExpectedConstraintsByType()
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);

        var constraints = await ReadRowsAsync(
            connection,
            "SELECT contype, count(*) FROM pg_constraint WHERE connamespace = 'fieldservice'::regnamespace GROUP BY contype ORDER BY contype",
            reader => (reader.GetChar(0), reader.GetInt64(1)));

        Assert.Equal([('c', 38L), ('f', 18L), ('p', 13L), ('u', 1L)], constraints);
    }

    [Fact]
    public async Task SchemaHoldsThirtySevenIndexesOfWhichTwentyOneAreUnique()
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);

        var indexes = await ReadRowsAsync(
            connection,
            "SELECT count(*), count(*) FILTER (WHERE i.indisunique) FROM pg_index i JOIN pg_class c ON c.oid = i.indrelid WHERE c.relnamespace = 'fieldservice'::regnamespace",
            reader => (reader.GetInt64(0), reader.GetInt64(1)));

        Assert.Equal([(37L, 21L)], indexes);
    }

    [Fact]
    public async Task EveryTableIsOwnedByTheMigratorAndGrantsTheAppRoleOnlyItsRowPrivileges()
    {
        const string ReadWrite = "SELECT,INSERT,UPDATE,DELETE";
        const string ReadOnly = "SELECT";
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);

        var tables = await ReadRowsAsync(
            connection,
            "SELECT c.relname, has_table_privilege('fieldservice_app', c.oid, 'SELECT'), has_table_privilege('fieldservice_app', c.oid, 'INSERT'), has_table_privilege('fieldservice_app', c.oid, 'UPDATE'), has_table_privilege('fieldservice_app', c.oid, 'DELETE'), has_table_privilege('fieldservice_app', c.oid, 'TRUNCATE'), pg_get_userbyid(c.relowner) FROM pg_class c WHERE c.relnamespace = 'fieldservice'::regnamespace AND c.relkind = 'r' ORDER BY c.relname",
            reader => (
                Table: reader.GetString(0),
                Privileges: string.Join(
                    ',',
                    new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE" }
                        .Where((_, index) => reader.GetBoolean(index + 1))),
                Owner: reader.GetString(6)));

        (string Table, string Privileges, string Owner)[] expected =
        [
            ("__EFMigrationsHistory", ReadOnly, DatabaseFixture.MigratorRole),
            ("checklist_items", ReadOnly, DatabaseFixture.MigratorRole),
            ("checklist_reading_fields", ReadOnly, DatabaseFixture.MigratorRole),
            ("checklist_templates", ReadOnly, DatabaseFixture.MigratorRole),
            ("inspection_answers", ReadWrite, DatabaseFixture.MigratorRole),
            ("inspection_readings", ReadWrite, DatabaseFixture.MigratorRole),
            ("inspections", ReadWrite, DatabaseFixture.MigratorRole),
            ("job_history", "SELECT,INSERT", DatabaseFixture.MigratorRole),
            ("jobs", ReadWrite, DatabaseFixture.MigratorRole),
            ("refresh_tokens", ReadWrite, DatabaseFixture.MigratorRole),
            ("technicians", ReadWrite, DatabaseFixture.MigratorRole),
            ("user_sessions", ReadWrite, DatabaseFixture.MigratorRole),
            ("users", ReadWrite, DatabaseFixture.MigratorRole),
        ];
        Assert.Equal(expected, tables);
    }

    [Fact]
    public async Task JobNumberSequenceGrantsTheAppRoleNothingAndStartsAt1001()
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);

        var privileges = await ReadRowsAsync(
            connection,
            "SELECT has_sequence_privilege('fieldservice_app', 'fieldservice.jobs_number_seq', 'USAGE'), has_sequence_privilege('fieldservice_app', 'fieldservice.jobs_number_seq', 'SELECT'), has_sequence_privilege('fieldservice_app', 'fieldservice.jobs_number_seq', 'UPDATE'), pg_get_userbyid(c.relowner) FROM pg_class c WHERE c.oid = 'fieldservice.jobs_number_seq'::regclass",
            reader => (reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3)));

        Assert.Equal([(false, false, false, DatabaseFixture.MigratorRole)], privileges);
        Assert.Equal(new DatabaseFixture.JobNumberSequenceState(1001, false), Fixture.InitialJobNumberSequence);
    }

    [Fact]
    public async Task FunctionsAreOwnedByTheMigratorWithAFixedSearchPathAndNarrowExecuteGrants()
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);

        var functions = await ReadRowsAsync(
            connection,
            "SELECT p.proname, pg_get_userbyid(p.proowner), p.prosecdef, array_to_string(p.proconfig, ';'), has_function_privilege('fieldservice_app', p.oid, 'EXECUTE'), has_function_privilege('public', p.oid, 'EXECUTE') FROM pg_proc p WHERE p.pronamespace = 'fieldservice'::regnamespace ORDER BY p.proname",
            reader => (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5)));

        (string, string, bool, string, bool, bool)[] expected =
        [
            ("advance_job_number", DatabaseFixture.MigratorRole, true, "search_path=fieldservice, pg_temp", true, false),
            ("refuse_submitted_inspection_change", DatabaseFixture.MigratorRole, false, "search_path=fieldservice, pg_temp", false, false),
        ];
        Assert.Equal(expected, functions);
    }

    [Fact]
    public async Task SubmittedInspectionTriggersAreEnabledOnTheThreeInspectionTables()
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);

        var triggers = await ReadRowsAsync(
            connection,
            "SELECT c.relname, t.tgname, t.tgenabled FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid WHERE c.relnamespace = 'fieldservice'::regnamespace AND NOT t.tgisinternal ORDER BY t.tgname",
            reader => (reader.GetString(0), reader.GetString(1), reader.GetChar(2)));

        (string, string, char)[] expected =
        [
            ("inspection_answers", "tr_inspection_answers_submitted_lock", 'O'),
            ("inspection_readings", "tr_inspection_readings_submitted_lock", 'O'),
            ("inspections", "tr_inspections_submitted_lock", 'O'),
        ];
        Assert.Equal(expected, triggers);
    }

    [Fact]
    public async Task RunDatabaseAndSchemaAreOwnedByTheMigrator()
    {
        await using var connection = await Fixture.OpenMigratorConnectionAsync(CancellationToken);

        var owners = await ReadRowsAsync(
            connection,
            "SELECT pg_get_userbyid(d.datdba), (SELECT pg_get_userbyid(n.nspowner) FROM pg_namespace n WHERE n.nspname = 'fieldservice') FROM pg_database d WHERE d.datname = current_database()",
            reader => (reader.GetString(0), reader.GetString(1)));

        Assert.Equal([(DatabaseFixture.MigratorRole, DatabaseFixture.MigratorRole)], owners);
    }

    private static IEnumerable<Guid> ExpectedTemplateIds()
    {
        return Enumerable.Range(1, _itemsPerTemplate.Length)
            .Select(template => Guid.Parse($"{IdPrefix}0001{template:D2}000000"));
    }

    private static IEnumerable<Guid> ExpectedItemIds()
    {
        return _itemsPerTemplate.SelectMany((count, index) => Enumerable.Range(1, count)
            .Select(item => Guid.Parse($"{IdPrefix}0002{index + 1:D2}{item:D2}0000")));
    }

    private static IEnumerable<Guid> ExpectedReadingFieldIds()
    {
        return _readingFields.Select(field =>
            Guid.Parse($"{IdPrefix}0003{field.Template:D2}{field.Item:D2}{field.Field:D2}00"));
    }

    private static async Task<List<T>> ReadRowsAsync<T>(
        NpgsqlConnection connection,
        string query,
        Func<NpgsqlDataReader, T> map)
    {
        await using var command = new NpgsqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
