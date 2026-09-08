namespace Zapara.Server.Timetable;

public enum FailureCode
{
    SnapshotMalformed, SourceRejected, SourceTimeout, DbUnavailable, PublicationUnknown, Cancelled, Abandoned
}

public static class FailureCodes
{
    public static string ToStorageCode(this FailureCode code) => code switch
    {
        FailureCode.SnapshotMalformed => "snapshot_malformed",
        FailureCode.SourceRejected => "source_rejected",
        FailureCode.SourceTimeout => "source_timeout",
        FailureCode.DbUnavailable => "db_unavailable",
        FailureCode.PublicationUnknown => "publication_unknown",
        FailureCode.Cancelled => "cancelled",
        FailureCode.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(code))
    };

    public static FailureCode Parse(string code) => code switch
    {
        "snapshot_malformed" => FailureCode.SnapshotMalformed,
        "source_rejected" => FailureCode.SourceRejected,
        "source_timeout" => FailureCode.SourceTimeout,
        "db_unavailable" => FailureCode.DbUnavailable,
        "publication_unknown" => FailureCode.PublicationUnknown,
        "cancelled" => FailureCode.Cancelled,
        "abandoned" => FailureCode.Abandoned,
        _ => throw new ArgumentException("Неизвестный код ошибки.")
    };
}
