using Vograph.Core.Models;

namespace Vograph.Desktop.Features.Homeworks;

internal sealed record HomeworkCompletionUndo(
    long Id, string Scope, bool BeforeDone, HomeworkCompletionUndo.TaskState After, DateTimeOffset ExpiresAt)
{
    internal sealed record TaskState(string Subject, string Text, DateTime CreatedAt, int TargetNthOccurrence,
        DateTime? Due, string Status, DateTime? DoneAt);

    public static HomeworkCompletionUndo Create(Homework before, Homework after, string scope, DateTimeOffset now) =>
        new(before.Id, scope, before.Status == "done", State(after), now.AddSeconds(5));

    public bool Allows(Homework? current, string scope, DateTimeOffset now) =>
        Scope == scope && now < ExpiresAt && current is not null && current.Id == Id && State(current) == After;

    private static TaskState State(Homework item) => new(item.SubjectRawNormalized, item.Text, item.CreatedAt,
        item.TargetNthOccurrence, item.DueDateComputed, item.Status, item.DoneAt);
}
