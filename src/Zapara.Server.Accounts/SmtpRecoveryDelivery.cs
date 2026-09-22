using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public interface IProductionRecoveryDelivery : IRecoveryDelivery { }

public sealed class SmtpRecoveryConfiguration
{
    public string Host { get; }
    public int Port { get; }
    public bool ImplicitTls { get; }
    public string From { get; }
    internal string? Username { get; }
    internal string? Password { get; }
    private SmtpRecoveryConfiguration(string host, int port, bool tls, string from, string? username, string? password)
        => (Host, Port, ImplicitTls, From, Username, Password) = (host, port, tls, from, username, password);
    public static SmtpRecoveryConfiguration? Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Accounts:Recovery:Smtp");
        if (!bool.TryParse(section["Enabled"], out var enabled) || !enabled) return null;
        var host = section["Host"];
        var mode = section["Security"] ?? "starttls";
        var portText = section["Port"] ?? (mode == "tls" ? "465" : "587");
        var username = section["Username"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown
            || !int.TryParse(portText, out var port) || port is < 1 or > 65535 || mode is not ("starttls" or "tls")
            || (string.IsNullOrEmpty(username) != string.IsNullOrEmpty(password))) return null;
        try { return new(host, port, mode == "tls", AccountValidation.Email(section["From"]), username, password); }
        catch (ArgumentException) { return null; }
    }
    public override string ToString() => "SmtpRecoveryConfiguration { [REDACTED] }";
}

public sealed record RecoveryMail(string Recipient, string Subject, string Body)
{
    public override string ToString() => "RecoveryMail { [REDACTED] }";
}

public interface IRecoverySmtpTransport
{
    Task SendAsync(SmtpRecoveryConfiguration configuration, RecoveryMail mail, CancellationToken ct);
}

public sealed class MailKitRecoveryTransport : IRecoverySmtpTransport
{
    public async Task SendAsync(SmtpRecoveryConfiguration configuration, RecoveryMail mail, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var client = new MailKit.Net.Smtp.SmtpClient();
        try
        {
            await client.ConnectAsync(configuration.Host, configuration.Port,
                configuration.ImplicitTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, deadline.Token);
            if (configuration.Username is not null)
                await client.AuthenticateAsync(configuration.Username, configuration.Password!, deadline.Token);
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Запара", configuration.From));
            message.To.Add(MailboxAddress.Parse(mail.Recipient));
            message.Subject = mail.Subject;
            message.Body = new TextPart("plain") { Text = mail.Body };
            await client.SendAsync(message, deadline.Token);
            await client.DisconnectAsync(true, deadline.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new AccountServiceException(AccountFailure.RecoveryUnavailable); }
    }
}

public sealed class SmtpRecoveryDelivery(SmtpRecoveryConfiguration configuration, IRecoverySmtpTransport transport) : IProductionRecoveryDelivery
{
    public Task SendVerificationAsync(Guid userId, string email, string token, CancellationToken ct)
        => Send(email, token, "Подтверждение почты Запара", "Код подтверждения почты", "30 минут", ct);
    public Task SendResetAsync(string email, string token, CancellationToken ct)
        => Send(email, token, "Восстановление доступа Запара", "Код восстановления доступа", "15 минут", ct);
    private Task Send(string email, string token, string subject, string action, string lifetime, CancellationToken ct)
    {
        var address = AccountValidation.Email(email);
        _ = RecoveryTokens.Hash(token);
        return transport.SendAsync(configuration, new(address, subject,
            $"{action}:\n\n{token}\n\nВведите этот код в приложении Запара. Он действует {lifetime} и используется один раз.\nЕсли вы не запрашивали действие, проигнорируйте письмо."), ct);
    }
    public override string ToString() => "SmtpRecoveryDelivery { [REDACTED] }";
}
