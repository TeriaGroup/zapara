using Xunit;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

public sealed class SyncSchemaShapeTests
{
    [Theory]
    [InlineData("sync_name", "acc_sync_name")]
    [InlineData("sync_acc_name", "acc_name")]
    [InlineData("select", "references")]
    public void Only_whole_quoted_or_unquoted_qualifiers_are_normalized(string sync, string accounts)
    {
        var options = SyncPostgresFixture.Options(sync, accounts);
        var source = $"{sync}.t {accounts}.u \"{sync}\".t \"{accounts}\".u";
        Assert.Equal("__SYNC__.t __ACCOUNTS__.u __SYNC__.t __ACCOUNTS__.u",
            SyncSchemaShape.NormalizeQualifiers(source, options));
        var untouched = $"x{sync}.t x{accounts}.u {sync}x.t {accounts}x.u ${sync}.t я{sync}.t \"x{sync}\".t \"x\"\"{sync}\".t '{sync}.t and ''{accounts}.u' E'escaped\\\'{sync}.t' $$ {sync}.t $$ $tag${accounts}.u$tag$ {sync} {accounts}";
        Assert.Equal(untouched, SyncSchemaShape.NormalizeQualifiers(untouched, options));
    }
}
