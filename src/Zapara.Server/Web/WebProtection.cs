using Microsoft.AspNetCore.DataProtection;

namespace Zapara.Server.Web;

internal static class WebProtection
{
    internal const string Key = "Zapara.Web";
    internal static IDataProtectionProvider Create(IServiceProvider services)
    {
        var path = services.GetRequiredService<IConfiguration>()["Web:DataProtectionKeysPath"];
        if (string.IsNullOrWhiteSpace(path))
            return services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Zapara.Web.Isolation.v1");
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Web:DataProtectionKeysPath должен быть абсолютным путём.");
        return DataProtectionProvider.Create(new DirectoryInfo(path), builder =>
        {
            builder.SetApplicationName("Zapara.Web");
            if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi();
        });
    }
}
