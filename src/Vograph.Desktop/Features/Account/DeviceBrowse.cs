using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Features.Account;

public static class DeviceBrowse
{
    public static IReadOnlyList<DeviceResponse> Filter(IEnumerable<DeviceResponse> devices, string query, int scope)
    {
        var words = query.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return devices.Where(device =>
        {
            if (scope == 1 && device.IsCurrent || scope == 2 && !device.IsCurrent) return false;
            var text = $"{device.DeviceName} {device.Platform} {device.DeviceId:N}".ToLowerInvariant().Replace('ё', 'е');
            return words.All(word => text.Contains(word, StringComparison.Ordinal));
        }).ToArray();
    }
}
