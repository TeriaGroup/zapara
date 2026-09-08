using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public sealed class OpaqueAccountHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AccountBearer";
    private bool unavailable;

    internal static string Bearer(HttpRequest request)
    {
        var values = request.Headers.Authorization;
        if (values.Count != 1 || values[0] is not { Length: 53 } header ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new AccountServiceException(AccountFailure.InvalidSession);
        try { return AccountValidation.Token(header[7..], "za_"); }
        catch (ArgumentException) { throw new AccountServiceException(AccountFailure.InvalidSession); }
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization")) return AuthenticateResult.NoResult();
        try
        {
            var bearer = Bearer(Request);
            var service = Context.RequestServices.GetRequiredService<AccountService>();
            var actor = await service.AuthenticateAsync(bearer, Context.RequestAborted);
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, actor.User.UserId.ToString("D")),
                new Claim("family_id", actor.FamilyId.ToString("D"))
            }, SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }
        catch (AccountServiceException exception)
        {
            unavailable = exception.Failure == AccountFailure.DbUnavailable;
            return AuthenticateResult.Fail(unavailable ? "db_unavailable" : "invalid_session");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => AccountErrors.Write(Context, unavailable ? 503 : 401, unavailable ? "db_unavailable" : "invalid_session");
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => AccountErrors.Write(Context, 403, "forbidden");
}
