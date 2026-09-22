namespace Vograph.Core.Models;

/// <summary>A saved local identifier plus its known university group name, resolved within one catalog generation.</summary>
public sealed record TimetableGroupRequest(string? Id, string? Name)
{
    public TimetableApiGroup Resolve(IEnumerable<TimetableApiGroup> groups)
    {
        var catalog = groups.ToArray();
        var direct = Id is null ? null : catalog.FirstOrDefault(g => g.Id == Id);
        var known = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
        if (direct is not null && (known is null || direct.Name.Equals(known, StringComparison.OrdinalIgnoreCase))) return direct;
        var wanted = known ?? Id;
        var matches = catalog.Where(g => g.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length != 1) throw new TimetableApiException(TimetableApiFailure.UnknownRequiredGroup);
        return matches[0];
    }
}
