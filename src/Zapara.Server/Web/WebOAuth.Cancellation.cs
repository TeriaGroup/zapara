using Npgsql;

namespace Zapara.Server.Web;

public sealed partial class WebOAuth
{
    internal async Task CancelAsync(HttpContext context, Guid id)
    {
        var browser = context.RequestServices.GetRequiredService<WebBrowserState>().Validate(context);
        await using var connection = source.CreateConnection();
        await connection.OpenAsync(context.RequestAborted);
        await using var command = new NpgsqlCommand($"""
            UPDATE {schema}.oauth_transactions t SET status='failed',subject=NULL,display_name=NULL,handoff_hash=NULL,handoff_expires_at=NULL
            FROM {schema}.web_oauth_flows w WHERE t.transaction_id=w.transaction_id
            AND t.transaction_id=@id AND w.browser_hash=@browser AND t.status IN ('pending','callbackClaimed','awaitingApp','failed','expired')
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("browser", WebConfiguration.Hash(browser.Nonce));
        if (await command.ExecuteNonQueryAsync(context.RequestAborted) == 0) throw new WebRequestException(409, "external_attempt_completed");
    }
}
