using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

[Collection("Account runtime")]
public sealed class GenericFileMetadataTests
{
    [Fact]
    public async Task Legacy_private_binary_and_pending_purge_are_denied_while_issued_public_links_still_work()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(Console.WriteLine, initialize: true);
        var objects = new MemoryObjectStore();
        var privateName = "00112233445566778899aabbccddeeff.bin";
        var genericName = "11112233445566778899aabbccddeeff.bin";
        var homeworkName = "hwf22112233445566778899aabbccddeeff.bin";
        foreach (var name in new[] { privateName, genericName, homeworkName }) objects.Put(name, [1, 2, 3]);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Accounts:Enabled", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:Enabled"] = "true", ["Accounts:Schema"] = db.Schema,
                ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"),
                ["Operator:Schema"] = db.Schema
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IObjectStore>();
                services.AddSingleton<IObjectStore>(objects);
            });
        });
        using var client = factory.CreateClient();
        var social = factory.Services.GetRequiredService<SocialConfiguration>().QuotedSchema;
        try
        {
            var accounts = factory.Services.GetRequiredService<AccountService>();
            var first = await AccountTestSupport.Seed(accounts, "file.owner");
            var second = await AccountTestSupport.Seed(accounts, "file.friend");
            var friend = Guid.NewGuid(); var conversation = Guid.NewGuid(); var message = Guid.NewGuid();
            await db.ExecuteAsync($"""
                INSERT INTO {social}.friendships VALUES ('{friend}','{first.User.UserId}','{second.User.UserId}','accepted','synthetic-pair',now());
                INSERT INTO {social}.conversations VALUES ('{conversation}','{friend}',now());
                INSERT INTO {social}.messages(message_id,conversation_id,sender_id,kind,body,created_at)
                  VALUES ('{message}','{conversation}','{first.User.UserId}','file','private.bin',now());
                INSERT INTO {social}.attachments(attachment_id,message_id,stored_name,original_name,content_type,byte_count)
                  VALUES ('{Guid.NewGuid()}','{message}','{privateName}','private.bin','application/octet-stream',3);
                """);
            foreach (var prefix in new[] { "/api/v1/files/", "/web-api/files/", "/api/v1/homework-files/", "/web-api/homework-files/" })
            {
                using var denied = await client.GetAsync(prefix + privateName, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            }
            Assert.Equal(new byte[] { 1, 2, 3 }, await client.GetByteArrayAsync("/api/v1/files/" + genericName, TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 1, 2, 3 }, await client.GetByteArrayAsync("/api/v1/homework-files/" + homeworkName, TestContext.Current.CancellationToken));
            await db.ExecuteAsync($"""
                INSERT INTO {social}.file_purge VALUES ('{privateName}',now());
                DELETE FROM {social}.attachments WHERE message_id='{message}';
                """);
            using var pending = await client.GetAsync("/api/v1/files/" + privateName, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            await db.ExecuteAsync($"DROP SCHEMA IF EXISTS {social} CASCADE");
        }
    }
}
