using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Contracts;

[Collection(DatabaseCollection.Name)]
public sealed partial class OpenApiSnapshotTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    public const string UpdateVariable = "FIELDSERVICE_UPDATE_CONTRACTS";

    public const string CiVariable = "CI";

    public const string DocumentPath = "/openapi/v1.json";

    public const string RewriteCommand =
        "FIELDSERVICE_UPDATE_CONTRACTS=1 dotnet test --project tests/FieldService.Api.Tests --filter-class FieldService.Api.Tests.Contracts.OpenApiSnapshotTests";

    private const int ReportedLineLimit = 40;

    private const int MaxEditDistance = 1000;

    private const int MaxDocumentDepth = 256;

    private static readonly JsonWriterOptions _canonicalWriterOptions = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = MaxDocumentDepth,
    };

    private static readonly JsonDocumentOptions _documentOptions = new() { MaxDepth = MaxDocumentDepth };

    public static string SnapshotPath => Path.Combine(RepositoryPaths.Root, "docs", "contracts", "openapi.v1.json");

    [Fact]
    public async Task ServedDocumentMatchesTheCommittedSnapshot()
    {
        var served = await ReadServedDocumentAsync(Fixture.Factory);

        var outcome = Check(served, SnapshotPath, SnapshotMode.FromEnvironment(Environment.GetEnvironmentVariable));

        TestContext.Current.TestOutputHelper?.WriteLine(outcome.Message);
        Assert.True(outcome.Passed, outcome.Message);
    }

    [Fact]
    public async Task UpdateModeWritesTheServedDocumentInCanonicalFormAndALaterCompareMatches()
    {
        using var snapshot = new TemporarySnapshot();
        var served = await ReadServedDocumentAsync(Fixture.Factory);

        var rewrite = Check(served, snapshot.Path, new SnapshotMode(Update: true, Ci: false));
        var compare = Check(served, snapshot.Path, new SnapshotMode(Update: false, Ci: false));

        Assert.True(rewrite.Passed, rewrite.Message);
        Assert.True(compare.Passed, compare.Message);
        var written = File.ReadAllText(snapshot.Path);
        Assert.Equal(Canonicalize(served), written);
        Assert.StartsWith("{\n  \"openapi\": \"3.1.", written, StringComparison.Ordinal);
        Assert.EndsWith("}\n", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", written, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareFailsOnAChangedMemberAndReportsBothLineNumbersAndTheRewriteCommand()
    {
        using var snapshot = new TemporarySnapshot();
        snapshot.Write("{\"info\":{\"title\":\"FieldService API\",\"version\":\"v1\"},\"paths\":{}}");

        var outcome = Check(
            "{\"info\":{\"title\":\"FieldService\",\"version\":\"v1\"},\"paths\":{}}",
            snapshot.Path,
            new SnapshotMode(Update: false, Ci: false));

        string[] expected =
        [
            "       3 |        | -     \"title\": \"FieldService API\",",
            "         |      3 | +     \"title\": \"FieldService\",",
        ];
        Assert.False(outcome.Passed);
        Assert.Equal(expected, ReportedDifferences(outcome.Message));
        Assert.Contains(RewriteCommand, outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAlignsAnInsertedMemberInsteadOfReportingEveryLaterLine()
    {
        using var snapshot = new TemporarySnapshot();
        snapshot.Write("{\"a\":1,\"c\":3,\"d\":4,\"e\":5}");

        var outcome = Check("{\"a\":1,\"b\":2,\"c\":3,\"d\":4,\"e\":5}", snapshot.Path, new SnapshotMode(false, false));

        string[] expected = ["         |      3 | +   \"b\": 2,"];
        Assert.False(outcome.Passed);
        Assert.Equal(expected, ReportedDifferences(outcome.Message));
    }

    [Fact]
    public void CompareReportsAtMostTheFirstFortyDifferingLines()
    {
        using var snapshot = new TemporarySnapshot();
        snapshot.Write(NumberedDocument(value: 1));

        var outcome = Check(NumberedDocument(value: 2), snapshot.Path, new SnapshotMode(false, false));

        Assert.False(outcome.Passed);
        var reported = ReportedDifferences(outcome.Message);
        Assert.Equal(ReportedLineLimit, reported.Count);
        Assert.Equal("       2 |        | -   \"member000\": 1,", reported[0]);
    }

    [Fact]
    public void CanonicalFormIgnoresIndentationLineEndingsAndEscapingButKeepsMemberOrder()
    {
        const string Compact = "{\"b\":\"caf\\u00e9 \\u003C\\u0026\\u003E\",\"a\":[1,2.50,{}]}";
        const string Pretty = "{\r\n    \"b\": \"café <&>\",\r\n    \"a\": [ 1, 2.50, { } ]\r\n}";
        const string Reordered = "{\"a\":[1,2.50,{}],\"b\":\"café <&>\"}";

        var canonical = Canonicalize(Compact);

        Assert.Equal("{\n  \"b\": \"café <&>\",\n  \"a\": [\n    1,\n    2.50,\n    {}\n  ]\n}\n", canonical);
        Assert.Equal(canonical, Canonicalize(Pretty));
        Assert.NotEqual(canonical, Canonicalize(Reordered));
    }

    [Fact]
    public void MissingSnapshotFailsWithAMessageNamingTheFileAndTheUpdateVariable()
    {
        using var snapshot = new TemporarySnapshot();

        var outcome = Check("{\"openapi\":\"3.1.1\"}", snapshot.Path, new SnapshotMode(Update: false, Ci: false));

        Assert.False(outcome.Passed);
        Assert.Contains($"{snapshot.Path} does not exist", outcome.Message, StringComparison.Ordinal);
        Assert.Contains($"{UpdateVariable}=1", outcome.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(snapshot.Path));
    }

    [Fact]
    public void UpdateModeIsRefusedWhenCiIsSetAndLeavesAnExistingSnapshotUntouched()
    {
        using var snapshot = new TemporarySnapshot();
        snapshot.Write("{\"openapi\":\"3.0.0\"}");
        var before = File.ReadAllBytes(snapshot.Path);

        var outcome = Check("{\"openapi\":\"3.1.1\"}", snapshot.Path, new SnapshotMode(Update: true, Ci: true));

        Assert.False(outcome.Passed);
        Assert.Contains($"{UpdateVariable}=1 is refused because {CiVariable} is set", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(snapshot.Path));
    }

    [Fact]
    public void UpdateModeIsRefusedWhenCiIsSetAndCreatesNoSnapshot()
    {
        using var snapshot = new TemporarySnapshot();

        var outcome = Check("{\"openapi\":\"3.1.1\"}", snapshot.Path, new SnapshotMode(Update: true, Ci: true));

        Assert.False(outcome.Passed);
        Assert.False(File.Exists(snapshot.Path));
    }

    [Theory]
    [InlineData(null, null, false, false)]
    [InlineData("1", null, true, false)]
    [InlineData("true", null, false, false)]
    [InlineData("0", null, false, false)]
    [InlineData(null, "true", false, true)]
    [InlineData("1", "1", true, true)]
    [InlineData(null, "", false, false)]
    public void ModeTreatsOnlyOneAsUpdateAndAnyNonEmptyCiValueAsCi(
        string? update,
        string? ci,
        bool expectedUpdate,
        bool expectedCi)
    {
        var variables = new Dictionary<string, string?> { [UpdateVariable] = update, [CiVariable] = ci };

        var mode = SnapshotMode.FromEnvironment(name => variables.GetValueOrDefault(name));

        Assert.Equal(new SnapshotMode(expectedUpdate, expectedCi), mode);
    }

    public static SnapshotOutcome Check(string servedDocument, string snapshotPath, SnapshotMode mode)
    {
        var served = Canonicalize(servedDocument);
        var shownPath = ShownPath(snapshotPath);

        if (mode.Update && mode.Ci)
        {
            return SnapshotOutcome.Fail(
                $"{UpdateVariable}=1 is refused because {CiVariable} is set: CI never rewrites {shownPath}. Rewrite it locally with {RewriteCommand}, review the diff and commit it.");
        }

        if (mode.Update)
        {
            var unchanged = File.Exists(snapshotPath) && TryCanonicalize(File.ReadAllText(snapshotPath)) == served;
            File.WriteAllText(snapshotPath, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return SnapshotOutcome.Pass(
                unchanged
                    ? $"{UpdateVariable}=1: {shownPath} already matched the served document and was written again."
                    : $"{UpdateVariable}=1: {shownPath} was rewritten from the served document. Review the diff before any client changes.");
        }

        if (!File.Exists(snapshotPath))
        {
            return SnapshotOutcome.Fail(
                $"{shownPath} does not exist. Create it from the served document by running this test with {UpdateVariable}=1: {RewriteCommand}. Then review the file and commit it.");
        }

        var committed = TryCanonicalize(File.ReadAllText(snapshotPath));
        if (committed is null)
        {
            return SnapshotOutcome.Fail($"{shownPath} is not valid JSON. Rewrite it with {RewriteCommand}.");
        }

        return committed == served
            ? SnapshotOutcome.Pass($"{shownPath} matches the served document.")
            : SnapshotOutcome.Fail(DescribeDifference(shownPath, committed, served));
    }

    public static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json, _documentOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, _canonicalWriterOptions))
        {
            document.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    public static async Task<string> ReadServedDocumentAsync(ApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync(DocumentPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(CancellationToken);
    }

    private static string? TryCanonicalize(string json)
    {
        try
        {
            return Canonicalize(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ShownPath(string path)
    {
        var relative = Path.GetRelativePath(RepositoryPaths.Root, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string DescribeDifference(string shownPath, string committed, string served)
    {
        var differences = DifferingLines(SplitLines(committed), SplitLines(served)).Take(ReportedLineLimit).ToList();
        var report = new StringBuilder()
            .Append(shownPath)
            .Append(" differs from the document the API serves at ")
            .Append(DocumentPath)
            .Append(" in the Testing environment. First ")
            .Append(differences.Count)
            .Append(" differing lines (- only in the snapshot, + only in the served document):\n")
            .Append("snapshot | served |\n");
        foreach (var difference in differences)
        {
            report.Append(difference.Format()).Append('\n');
        }

        return report
            .Append("If the change is intended, rewrite the snapshot with ")
            .Append(RewriteCommand)
            .Append(", then review the diff and have it approved before any client changes.")
            .ToString();
    }

    private static string[] SplitLines(string canonical)
    {
        return canonical.TrimEnd('\n').Split('\n');
    }

    private static IEnumerable<DiffLine> DifferingLines(string[] snapshot, string[] served)
    {
        var prefix = 0;
        while (prefix < snapshot.Length && prefix < served.Length && snapshot[prefix] == served[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < snapshot.Length - prefix
            && suffix < served.Length - prefix
            && snapshot[snapshot.Length - 1 - suffix] == served[served.Length - 1 - suffix])
        {
            suffix++;
        }

        var snapshotMiddle = snapshot[prefix..(snapshot.Length - suffix)];
        var servedMiddle = served[prefix..(served.Length - suffix)];
        var script = ShortestEditScript(snapshotMiddle, servedMiddle);
        if (script is null)
        {
            return snapshotMiddle
                .Select((text, index) => new DiffLine(prefix + index + 1, null, text))
                .Concat(servedMiddle.Select((text, index) => new DiffLine(null, prefix + index + 1, text)));
        }

        return script.Select(edit => edit.Removed
            ? new DiffLine(prefix + edit.Index + 1, null, snapshotMiddle[edit.Index])
            : new DiffLine(null, prefix + edit.Index + 1, servedMiddle[edit.Index]));
    }

    private static List<Edit>? ShortestEditScript(string[] before, string[] after)
    {
        var limit = Math.Min(before.Length + after.Length, MaxEditDistance);
        var offset = limit + 1;
        var furthest = new int[(2 * limit) + 3];
        var trace = new List<int[]>();
        for (var distance = 0; distance <= limit; distance++)
        {
            for (var diagonal = -distance; diagonal <= distance; diagonal += 2)
            {
                var x = MovesDown(furthest, offset, diagonal, distance)
                    ? furthest[offset + diagonal + 1]
                    : furthest[offset + diagonal - 1] + 1;
                var y = x - diagonal;
                while (x < before.Length && y < after.Length && before[x] == after[y])
                {
                    x++;
                    y++;
                }

                furthest[offset + diagonal] = x;
                if (x >= before.Length && y >= after.Length)
                {
                    trace.Add((int[])furthest.Clone());
                    return Backtrack(trace, before.Length, after.Length, offset);
                }
            }

            trace.Add((int[])furthest.Clone());
        }

        return null;
    }

    private static List<Edit> Backtrack(List<int[]> trace, int beforeLength, int afterLength, int offset)
    {
        var edits = new List<Edit>();
        var x = beforeLength;
        var y = afterLength;
        for (var distance = trace.Count - 1; distance > 0; distance--)
        {
            var previous = trace[distance - 1];
            var diagonal = x - y;
            var previousDiagonal = MovesDown(previous, offset, diagonal, distance) ? diagonal + 1 : diagonal - 1;
            var previousX = previous[offset + previousDiagonal];
            var previousY = previousX - previousDiagonal;
            while (x > previousX && y > previousY)
            {
                x--;
                y--;
            }

            edits.Add(x == previousX ? new Edit(Removed: false, y - 1) : new Edit(Removed: true, x - 1));
            x = previousX;
            y = previousY;
        }

        edits.Reverse();
        return edits;
    }

    private static bool MovesDown(int[] furthest, int offset, int diagonal, int distance)
    {
        return diagonal == -distance
            || (diagonal != distance && furthest[offset + diagonal - 1] < furthest[offset + diagonal + 1]);
    }

    private static List<string> ReportedDifferences(string message)
    {
        return [.. message.Split('\n').Where(line => ReportedLinePattern().IsMatch(line))];
    }

    private static string NumberedDocument(int value)
    {
        return "{" + string.Join(",", Enumerable.Range(0, 60).Select(index => $"\"member{index:000}\":{value}")) + "}";
    }

    [GeneratedRegex(@"^ {0,8}\d* \| {0,7}\d* \| [-+] ")]
    private static partial Regex ReportedLinePattern();

    public sealed record SnapshotMode(bool Update, bool Ci)
    {
        public static SnapshotMode FromEnvironment(Func<string, string?> read)
        {
            return new SnapshotMode(read(UpdateVariable) == "1", !string.IsNullOrEmpty(read(CiVariable)));
        }
    }

    public sealed record SnapshotOutcome(bool Passed, string Message)
    {
        public static SnapshotOutcome Pass(string message) => new(true, message);

        public static SnapshotOutcome Fail(string message) => new(false, message);
    }

    private sealed record Edit(bool Removed, int Index);

    private sealed record DiffLine(int? SnapshotLine, int? ServedLine, string Text)
    {
        public string Format()
        {
            var sign = SnapshotLine is null ? '+' : '-';
            return $"{SnapshotLine,8} | {ServedLine,6} | {sign} {Text}";
        }
    }

    private sealed class TemporarySnapshot : IDisposable
    {
        private readonly string _directory =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fieldservice-api-tests", Guid.NewGuid().ToString("N"));

        public TemporarySnapshot()
        {
            Directory.CreateDirectory(_directory);
        }

        public string Path => System.IO.Path.Combine(_directory, "openapi.v1.json");

        public void Write(string content)
        {
            File.WriteAllText(Path, content);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            Directory.Delete(_directory);
        }
    }
}
