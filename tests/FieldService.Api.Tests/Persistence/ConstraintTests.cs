using FieldService.Api.Domain;
using FieldService.Api.Persistence.Seeding;
using FieldService.Api.Tests.Infrastructure;
using Npgsql;

namespace FieldService.Api.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class ConstraintTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private const string InsertAnswer =
        "INSERT INTO fieldservice.inspection_answers (inspection_id, checklist_item_id, result, note, answered_at) VALUES (@inspection_id, ";

    private const string InsertReading =
        "INSERT INTO fieldservice.inspection_readings (inspection_id, checklist_item_id, reading_field_id, value) VALUES (@inspection_id, ";

    private const string InsertInspection =
        "INSERT INTO fieldservice.inspections (id, job_id, technician_id, created_at, updated_at, submitted_at, technician_notes) VALUES (@new_id, ";

    private const string UpdateDraft = "UPDATE fieldservice.inspections SET ";

    private const string DraftAnswer = " WHERE inspection_id = @inspection_id AND checklist_item_id = ";

    private const string NoteCheck = "ck_inspection_answers_note";

    private const string ResultCheck = "ck_inspection_answers_result";

    private const string NotesCheck = "ck_inspections_technician_notes";

    private const string TimestampsCheck = "ck_inspections_timestamps";

    private static readonly Guid _hvacTemplateId = ChecklistTemplateSeed.TemplateId(2);

    private static readonly Dictionary<string, (string Sql, string SqlState, string? Constraint)> _rejected = new(StringComparer.Ordinal)
    {
        ["C01 Fail answer with a null note"] = (InsertAnswer + "@item7, 'Fail', NULL, @at)", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C02 Fail answer with a note of three spaces"] = (InsertAnswer + "@item7, 'Fail', '   ', @at)", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C03 Fail answer with an empty note"] = (InsertAnswer + "@item7, 'Fail', '', @at)", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C04 Pass answer with an empty note"] = (InsertAnswer + "@item7, 'Pass', '', @at)", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C05 NotApplicable answer with a note of two spaces"] = (InsertAnswer + "@item7, 'NotApplicable', '  ', @at)", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C10 result Unknown"] = (InsertAnswer + "@item7, 'Unknown', NULL, @at)", PostgresErrorCodes.CheckViolation, ResultCheck),
        ["C11 result pass in lower case"] = (InsertAnswer + "@item7, 'pass', NULL, @at)", PostgresErrorCodes.CheckViolation, ResultCheck),
        ["C12 result N/A as displayed"] = (InsertAnswer + "@item7, 'N/A', NULL, @at)", PostgresErrorCodes.CheckViolation, ResultCheck),
        ["C13 note of 1001 characters"] = (InsertAnswer + "@item7, 'Fail', repeat('x', 1001), @at)", PostgresErrorCodes.StringDataRightTruncation, null),
        ["C16 saved Fail answer whose note is cleared"] = ("UPDATE fieldservice.inspection_answers SET note = NULL" + DraftAnswer + "@item5", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C17 saved Fail answer whose note becomes spaces"] = ("UPDATE fieldservice.inspection_answers SET note = '  '" + DraftAnswer + "@item5", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C18 saved Pass answer turned into a Fail without a note"] = ("UPDATE fieldservice.inspection_answers SET result = 'Fail'" + DraftAnswer + "@item4", PostgresErrorCodes.CheckViolation, NoteCheck),
        ["C20 technician notes on a draft"] = (UpdateDraft + "technician_notes = 'Done' WHERE id = @inspection_id", PostgresErrorCodes.CheckViolation, NotesCheck),
        ["C21 submission with blank technician notes"] = (UpdateDraft + "submitted_at = updated_at, technician_notes = '  ' WHERE id = @inspection_id", PostgresErrorCodes.CheckViolation, NotesCheck),
        ["C23 submitted_at before created_at"] = (UpdateDraft + "submitted_at = created_at - interval '1 millisecond' WHERE id = @inspection_id", PostgresErrorCodes.CheckViolation, TimestampsCheck),
        ["C24 updated_at before created_at"] = (UpdateDraft + "updated_at = created_at - interval '1 millisecond' WHERE id = @inspection_id", PostgresErrorCodes.CheckViolation, TimestampsCheck),
        ["C26 technician notes of 2001 characters"] = (UpdateDraft + "submitted_at = updated_at, technician_notes = repeat('x', 2001) WHERE id = @inspection_id", PostgresErrorCodes.StringDataRightTruncation, null),
        ["U01 second inspection for a job"] = (InsertInspection + "@job_id, @technician_id, @at, @at, NULL, NULL)", PostgresErrorCodes.UniqueViolation, "ix_inspections_job_id"),
        ["U02 second answer for an item"] = (InsertAnswer + "@item1, 'Pass', NULL, @at)", PostgresErrorCodes.UniqueViolation, "pk_inspection_answers"),
        ["U03 second reading for a field"] = (InsertReading + "@item3, @suction, 120)", PostgresErrorCodes.UniqueViolation, "pk_inspection_readings"),
        ["F01 reading with the field of another item"] = (InsertReading + "@item3, @supply, 54)", PostgresErrorCodes.ForeignKeyViolation, "fk_inspection_readings_checklist_reading_fields"),
        ["F02 reading for an unanswered item"] = (InsertReading + "@item8, @supply, 54)", PostgresErrorCodes.ForeignKeyViolation, "fk_inspection_readings_inspection_answers"),
        ["F03 answer for an item that does not exist"] = (InsertAnswer + "@unknown_item, 'Pass', NULL, @at)", PostgresErrorCodes.ForeignKeyViolation, "fk_inspection_answers_checklist_items_checklist_item_id"),
        ["F04 inspection for a job that does not exist"] = (InsertInspection + "@new_id, @technician_id, @at, @at, NULL, NULL)", PostgresErrorCodes.ForeignKeyViolation, "fk_inspections_jobs_job_id"),
        ["F05 inspection whose technician is not a technician"] = (InsertInspection + "@spare_job_id, @administrator_id, @at, @at, NULL, NULL)", PostgresErrorCodes.ForeignKeyViolation, "fk_inspections_technicians_technician_id"),
        ["K01 delete a job that has an inspection"] = ("DELETE FROM fieldservice.jobs WHERE id = @job_id", PostgresErrorCodes.ForeignKeyViolation, "fk_inspections_jobs_job_id"),
        ["N03 reading 99999999.995"] = ("UPDATE fieldservice.inspection_readings SET value = 99999999.995" + DraftAnswer + "@item3 AND reading_field_id = @suction", PostgresErrorCodes.NumericValueOutOfRange, null),
        ["N04 reading 100000000"] = ("UPDATE fieldservice.inspection_readings SET value = 100000000" + DraftAnswer + "@item3 AND reading_field_id = @suction", PostgresErrorCodes.NumericValueOutOfRange, null),
        ["N05 reading -99999999.995"] = ("UPDATE fieldservice.inspection_readings SET value = -99999999.995" + DraftAnswer + "@item3 AND reading_field_id = @suction", PostgresErrorCodes.NumericValueOutOfRange, null),
        ["T07 delete a submitted inspection"] = ("DELETE FROM fieldservice.inspections WHERE id = @submitted_id", PostgresErrorCodes.ObjectNotInPrerequisiteState, null),
        ["T12 submit a draft and then change its answer in the same transaction"] = (
            UpdateDraft + "submitted_at = updated_at WHERE id = @inspection_id; UPDATE fieldservice.inspection_answers SET note = 'late'" + DraftAnswer + "@item5",
            PostgresErrorCodes.ObjectNotInPrerequisiteState,
            null),
    };

    private static readonly Dictionary<string, string> _accepted = new(StringComparer.Ordinal)
    {
        ["C06 Fail answer with a tab as its note"] = InsertAnswer + "@item7, 'Fail', E'\\t', @at)",
        ["C07 Fail answer with a line feed as its note"] = InsertAnswer + "@item7, 'Fail', E'\\n', @at)",
        ["C08 Fail answer with a no-break space as its note"] = InsertAnswer + "@item7, 'Fail', chr(160), @at)",
        ["C09 Fail answer with a note"] = InsertAnswer + "@item7, 'Fail', 'Drain line blocked', @at)",
        ["C14 note of 1000 two-byte characters"] = InsertAnswer + "@item7, 'Fail', repeat(chr(233), 1000), @at)",
        ["C15 note of 1000 characters outside the BMP"] = InsertAnswer + "@item7, 'Fail', repeat(chr(128295), 1000), @at)",
        ["C22 submission without technician notes"] = UpdateDraft + "submitted_at = updated_at, technician_notes = NULL WHERE id = @inspection_id",
        ["C25 submitted_at equal to created_at"] = UpdateDraft + "submitted_at = created_at WHERE id = @inspection_id",
        ["N01 reading 99999999.99"] = "UPDATE fieldservice.inspection_readings SET value = 99999999.99" + DraftAnswer + "@item3 AND reading_field_id = @suction",
        ["N02 reading -99999999.99"] = "UPDATE fieldservice.inspection_readings SET value = -99999999.99" + DraftAnswer + "@item3 AND reading_field_id = @suction",
    };

    public static TheoryData<string> RejectedProbes => [.. _rejected.Keys];

    public static TheoryData<string> AcceptedProbes => [.. _accepted.Keys];

    [Theory]
    [MemberData(nameof(RejectedProbes))]
    public async Task AStatementThatBreaksAnInspectionRuleFailsWithItsSqlStateAndConstraint(string probe)
    {
        var (sql, sqlState, constraint) = _rejected[probe];
        var rows = await ArrangeAsync();
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = Command(sql, connection, transaction, rows);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken));

        Assert.Equal(sqlState, exception.SqlState);
        Assert.Equal(constraint, string.IsNullOrEmpty(exception.ConstraintName) ? null : exception.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(AcceptedProbes))]
    public async Task AStatementAtTheEdgeOfAnInspectionRuleSucceedsInARolledBackTransaction(string probe)
    {
        var rows = await ArrangeAsync();
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = Command(_accepted[probe], connection, transaction, rows);

        var affected = await command.ExecuteNonQueryAsync(CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task AReadingWithAThirdDecimalIsRoundedByTheColumnSoTheApiMustRefuseItFirst()
    {
        var rows = await ArrangeAsync();
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var update = Command(
            "UPDATE fieldservice.inspection_readings SET value = 99999999.994" + DraftAnswer + "@item3 AND reading_field_id = @suction",
            connection,
            transaction,
            rows);
        await update.ExecuteNonQueryAsync(CancellationToken);
        await using var read = Command(
            "SELECT value::text FROM fieldservice.inspection_readings" + DraftAnswer + "@item3 AND reading_field_id = @suction",
            connection,
            transaction,
            rows);

        var stored = (string?)await read.ExecuteScalarAsync(CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal("99999999.99", stored);
    }

    [Fact]
    public async Task DeletingAnAnswerCascadesToItsReadingsOnly()
    {
        var rows = await ArrangeAsync();

        var (answers, readings) = await CountsAfterAsync(rows, "DELETE FROM fieldservice.inspection_answers" + DraftAnswer + "@item3");

        Assert.Equal((3, 0), (answers, readings));
    }

    [Fact]
    public async Task DeletingADraftInspectionCascadesToItsAnswersAndReadings()
    {
        var rows = await ArrangeAsync();

        var (answers, readings) = await CountsAfterAsync(rows, "DELETE FROM fieldservice.inspections WHERE id = @inspection_id");

        Assert.Equal((0, 0), (answers, readings));
    }

    [Fact]
    public async Task ReplacingAPassWithReadingsByANotApplicableAnswerLeavesTheAnswerWithoutReadings()
    {
        var rows = await ArrangeAsync();

        var (answers, readings) = await CountsAfterAsync(
            rows,
            "UPDATE fieldservice.inspection_answers SET result = 'NotApplicable', note = 'Not needed'" + DraftAnswer + "@item3; "
                + "DELETE FROM fieldservice.inspection_readings" + DraftAnswer + "@item3");

        Assert.Equal((4, 0), (answers, readings));
    }

    private async Task<(int Answers, int Readings)> CountsAfterAsync(Rows rows, string statement)
    {
        await using var connection = await Fixture.OpenAppConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var change = Command(statement, connection, transaction, rows);
        await change.ExecuteNonQueryAsync(CancellationToken);
        await using var count = Command(
            "SELECT (SELECT count(*) FROM fieldservice.inspection_answers WHERE inspection_id = @inspection_id)::int, "
                + "(SELECT count(*) FROM fieldservice.inspection_readings WHERE inspection_id = @inspection_id)::int",
            connection,
            transaction,
            rows);
        await using var reader = await count.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        var counts = (reader.GetInt32(0), reader.GetInt32(1));
        await reader.CloseAsync();
        await transaction.RollbackAsync(CancellationToken);
        return counts;
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction, Rows rows)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in rows.Parameters)
        {
            if (sql.Contains("@" + name, StringComparison.Ordinal))
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
        var draftJob = await new TestData(Fixture, now.AddMinutes(-25)).MoveToAsync(
            await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken),
            technician,
            JobStatus.InProgress,
            CancellationToken);
        var draft = await new TestData(Fixture, now.AddMinutes(-20)).SaveInspectionAsync(
            draftJob,
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
        var submitted = await new TestData(Fixture, now.AddMinutes(-20)).CreateInspectionAsync(
            await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken),
            technician,
            submitted: true,
            CancellationToken);
        var spareJob = await data.CreateJobAsync(administrator.Id, _hvacTemplateId, CancellationToken);
        return new Rows(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["inspection_id"] = draft.Id,
            ["submitted_id"] = submitted.Id,
            ["job_id"] = draftJob.Id,
            ["spare_job_id"] = spareJob.Id,
            ["technician_id"] = technician.UserId,
            ["administrator_id"] = administrator.Id,
            ["new_id"] = Guid.CreateVersion7(),
            ["at"] = now,
            ["item1"] = Item(1),
            ["item3"] = Item(3),
            ["item4"] = Item(4),
            ["item5"] = Item(5),
            ["item7"] = Item(7),
            ["item8"] = Item(8),
            ["unknown_item"] = ChecklistTemplateSeed.ItemId(2, 11),
            ["suction"] = Field(3, 1),
            ["supply"] = Field(8, 1),
        });
    }

    private static Guid Item(int position) => ChecklistTemplateSeed.ItemId(2, position);

    private static Guid Field(int item, int position) => ChecklistTemplateSeed.ReadingFieldId(2, item, position);

    private sealed record Rows(IReadOnlyDictionary<string, object> Parameters);
}
