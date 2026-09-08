using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Tests;

public sealed class CommunityContractTests
{
    [Fact]
    public void Results_contract_has_aggregates_only()
    {
        var names = typeof(PollResultsResponse).GetProperties().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "Options", "PollId", "TotalVotes" }, names);
        Assert.DoesNotContain("UserId", typeof(PollOptionResult).GetProperties().Select(p => p.Name));
        Assert.Null(typeof(PollResultsResponse).GetProperty("Votes"));
        Assert.Null(typeof(PollResultsResponse).GetProperty("Voters"));
        var json = JsonSerializer.Serialize(new PollResultsResponse(Guid.NewGuid(), 2,
            [new(Guid.NewGuid(), "Да", 1), new(Guid.NewGuid(), "Нет", 1)]), CommunityJson.CreateOptions());
        using var document = JsonDocument.Parse(json);
        ApiTestFactory.Keys(document.RootElement, "pollId", "totalVotes", "options");
        Assert.False(json.Contains("userId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddCommunities_and_MapCommunities_signatures_are_integrator_ready()
    {
        var add = typeof(Zapara.Server.Communities.CommunitiesRegistration).GetMethod("AddCommunities");
        var map = typeof(Zapara.Server.Communities.CommunitiesRegistration).GetMethod("MapCommunities");
        Assert.NotNull(add);
        Assert.NotNull(map);
        Assert.Equal(typeof(IServiceCollection), add!.ReturnType);
        Assert.Equal(typeof(WebApplication), map!.ReturnType);
        Assert.Equal(new[] { typeof(IServiceCollection), typeof(IConfiguration) }, add.GetParameters().Select(p => p.ParameterType).ToArray());
        Assert.Equal(new[] { typeof(WebApplication) }, map.GetParameters().Select(p => p.ParameterType).ToArray());
    }
}
