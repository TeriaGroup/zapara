using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

public sealed class SocialConfiguration
{
    private SocialConfiguration(string schema, string accountsSchema, string mediaRoot)
    {
        Schema = schema;
        QuotedSchema = "\"" + schema + "\"";
        QuotedAccounts = "\"" + accountsSchema + "\"";
        MediaRoot = mediaRoot;
    }

    public string Schema { get; }
    public string QuotedSchema { get; }
    public string QuotedAccounts { get; }
    public string MediaRoot { get; }

    public static SocialConfiguration Create(AccountsConfiguration accounts, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(configuration);
        var schema = accounts.Schema == "accounts" ? "social" : accounts.Schema + "_social";
        if (!Regex.IsMatch(schema, @"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Недопустимая схема переписки.");
        var configured = configuration["Social:MediaRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zapara", "social-media")
            : configured.Trim();
        var full = Path.GetFullPath(root);
        var drive = Path.GetPathRoot(full) ?? "";
        if (drive.Length == 0 || string.Equals(
            full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            drive.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Недопустимый каталог вложений.");
        return new(schema, accounts.Schema, full);
    }
}
