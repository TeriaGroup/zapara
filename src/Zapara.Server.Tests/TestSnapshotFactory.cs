using System.Text;
using Vograph.Timetable;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public static class TestSnapshotFactory
{
    public static ValidatedSnapshot Create(TimeProvider clock, bool smaller = false, SourceKind kind = SourceKind.File)
    {
        var bytes = Encoding.UTF8.GetBytes(ContractTests.Fixture(smaller ? "valid-b.xml" : "valid-a.xml"));
        var source = SourceDocument.Create(bytes, kind, clock,
            kind == SourceKind.Http ? clock.GetUtcNow().AddHours(-1) : null);
        var (groups, lessons, start, weeks, title) = new TimetableParser().Parse(source.DecodedXml);
        var snapshot = SnapshotMapper.FromParsed(groups, lessons, start, weeks, title, source);
        Array.Fill(bytes, (byte)0);
        groups[0].Name = "mutated";
        lessons[0].SubjectRaw = "mutated";
        groups.Clear();
        lessons.Clear();
        return snapshot;
    }
}
