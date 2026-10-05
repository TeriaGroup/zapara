using Vograph.Core.Services.Accounts;
using Xunit;

namespace Vograph.Desktop.Tests;

public class AccountClientExceptionTests
{
    [Theory]
    [InlineData(AccountClientFailure.InvalidExternalProof, "invalid_external_proof", 403)]
    [InlineData(AccountClientFailure.ProviderUnavailable, "provider_unavailable", 503)]
    public void Code_exposes_server_failure_strings(AccountClientFailure failure, string code, int status)
    {
        var error = new AccountClientException(failure, status);
        Assert.Equal(failure, error.Failure);
        Assert.Equal(status, error.Status);
        Assert.Equal(code, error.Code);
        Assert.Equal("Операция аккаунта не выполнена.", error.Message);
    }

    [Theory]
    [InlineData(AccountClientFailure.InvalidRequest, "invalid_request")]
    [InlineData(AccountClientFailure.NotConfigured, "not_configured")]
    [InlineData(AccountClientFailure.ReauthenticationRequired, "account_operation_failed")]
    public void Existing_codes_stay_stable(AccountClientFailure failure, string code)
        => Assert.Equal(code, new AccountClientException(failure).Code);
}
