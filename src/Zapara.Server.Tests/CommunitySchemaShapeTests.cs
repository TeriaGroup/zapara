using Xunit;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed class CommunitySchemaShapeTests
{
    [Theory]
    [InlineData("com_name", "acc_com_name")]
    [InlineData("com_acc_name", "acc_name")]
    [InlineData("select", "references")]
    public void Only_whole_quoted_or_unquoted_qualifiers_are_normalized(string communities, string accounts)
    {
        var options = CommunityPostgresFixture.Options(communities, accounts);
        var source = $"{communities}.t {accounts}.u \"{communities}\".t \"{accounts}\".u";
        Assert.Equal("__COM__.t __ACCOUNTS__.u __COM__.t __ACCOUNTS__.u",
            CommunitiesSchemaShape.NormalizeQualifiers(source, options));
        var untouched = $"x{communities}.t x{accounts}.u {communities}x.t {accounts}x.u ${communities}.t я{communities}.t \"x{communities}\".t \"x\"\"{communities}\".t '{communities}.t and ''{accounts}.u' E'escaped\\\'{communities}.t' $$ {communities}.t $$ $tag${accounts}.u$tag$ {communities} {accounts}";
        Assert.Equal(untouched, CommunitiesSchemaShape.NormalizeQualifiers(untouched, options));
    }
}
