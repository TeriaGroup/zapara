namespace Zapara.Server.Accounts;

public interface IContentArchive
{
    void Put(string key, byte[] bytes);
    byte[]? Get(string key);
    void Delete(string key);
}

public interface IUploadQuota
{
    Task<byte[]> Accept(IAccountUnitOfWork accounts, string token, string? groupId, string key, byte[] bytes, CancellationToken ct);
}

public static class ContentNames
{
    public static string Text(Guid id) => "text" + id.ToString("N");
    public static string GroupMessage(Guid id) => "gmsg" + id.ToString("N");
    public static string GroupFile(Guid id) => "gfile" + id.ToString("N");
    public static string Homework(Guid id) => "hw" + id.ToString("N");
    public static string HomeworkFile(Guid id) => "hwf" + id.ToString("N") + ".bin";
    public static string Support(Guid id) => "sup" + id.ToString("N");
}
