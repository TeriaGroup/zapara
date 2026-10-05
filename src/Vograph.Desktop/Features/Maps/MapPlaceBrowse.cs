using Vograph.Core.Campus;

namespace Vograph.Desktop.Features.Maps;

public sealed record MapPlaceChoice(string Id, string Label, string Building, int Floor, string Room);

public static class MapPlaceBrowse
{
    public static IReadOnlyList<MapPlaceChoice> Filter(IEnumerable<Node> nodes, string query)
    {
        var words = query.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return nodes.Where(node => node.Kind == "room" && !string.IsNullOrWhiteSpace(node.Room))
            .Select(node => new MapPlaceChoice(node.Id,
                $"{node.Room} · {node.Building}, {node.Floor} этаж", node.Building, node.Floor, node.Room!))
            .Where(place => words.All(word =>
                (place.Label + " " + place.Id).ToLowerInvariant().Replace('ё', 'е').Contains(word, StringComparison.Ordinal)))
            .OrderBy(place => place.Building, StringComparer.Ordinal)
            .ThenBy(place => place.Floor).ThenBy(place => place.Room, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
