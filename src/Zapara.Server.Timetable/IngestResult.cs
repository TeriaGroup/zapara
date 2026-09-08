namespace Zapara.Server.Timetable;

public sealed record IngestCounts(int Groups, int Lessons);

public sealed record IngestResult(int ExitCode, Guid? AttemptId = null, Guid? SnapshotId = null,
    IngestCounts? Counts = null, FailureCode? FailureCode = null, FailureCode? CleanupFailureCode = null)
{
    public static IngestResult Failed(FailureCode code, Guid? attemptId = null) => new(code switch
    {
        global::Zapara.Server.Timetable.FailureCode.SnapshotMalformed or global::Zapara.Server.Timetable.FailureCode.SourceRejected => 2,
        global::Zapara.Server.Timetable.FailureCode.SourceTimeout => 4,
        global::Zapara.Server.Timetable.FailureCode.Cancelled => 6,
        _ => 5
    }, attemptId, FailureCode: code);
}
