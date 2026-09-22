namespace Vograph.Desktop.Legal;

public sealed record LegalText(string Id, string Title, string Body);

/// <summary>The same files the web client renders. Settings and account entry show these strings.</summary>
public static class LegalDocuments
{
    public const string AgreementTitle = "Пользовательское соглашение";
    public const string PolicyTitle = "Политика обработки персональных данных";

    public static LegalText Agreement { get; } = Load("agreement", AgreementTitle, "legal.user-agreement.txt");
    public static LegalText Policy { get; } = Load("policy", PolicyTitle, "legal.privacy-policy.txt");

    public static LegalText Open(string id) => id == "agreement" ? Agreement : Policy;

    private static LegalText Load(string id, string title, string name)
    {
        using var stream = typeof(LegalDocuments).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream);
        var body = reader.ReadToEnd().TrimStart('\uFEFF').Trim();
        return new LegalText(id, title, body);
    }
}
