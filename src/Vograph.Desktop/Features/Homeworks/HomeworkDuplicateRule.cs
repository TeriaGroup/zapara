using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Homeworks;

public static class HomeworkDuplicateRule
{
    public static bool Exists(IEnumerable<Homework> existing, string subjectRaw, string text, DateTime? due)
    {
        var subject = ParityService.NormalizeSubject(subjectRaw);
        var body = text.Trim();
        return existing.Any(item => !item.Tombstone &&
            item.SubjectRawNormalized.Equals(subject, StringComparison.OrdinalIgnoreCase) &&
            item.Text.Trim().Equals(body, StringComparison.OrdinalIgnoreCase) &&
            item.DueDateComputed?.Date == due?.Date);
    }
}
