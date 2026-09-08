namespace Zapara.Server.Timetable;

public sealed class StoreException(FailureCode failureCode, Guid? attemptId = null)
    : Exception("Операция хранилища расписания не выполнена.")
{
    public FailureCode FailureCode { get; } = failureCode;
    public Guid? AttemptId { get; } = attemptId;
}
