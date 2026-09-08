using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Text.RegularExpressions;

namespace Zapara.Server.Timetable;

public sealed class TimetableConfiguration
{
    private readonly string connectionString;

    private TimetableConfiguration(string connectionString, string schema)
    {
        this.connectionString = connectionString;
        Schema = schema;
    }

    public string Schema { get; }
    public string QuotedSchema => $"\"{Schema}\"";

    public static TimetableConfiguration FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration.GetConnectionString("Timetable");
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Не задан ConnectionStrings:Timetable.");
        var schema = configuration["Timetable:Schema"];
        // \z rejects a trailing newline, unlike the permissive .NET $ anchor.
        if (schema is null || !Regex.IsMatch(schema, @"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Недопустимый Timetable:Schema.");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(raw)
            {
                Pooling = true,
                IncludeErrorDetail = false,
                LogParameters = false,
                PersistSecurityInfo = false
            };
            return new TimetableConfiguration(builder.ConnectionString, schema);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Недопустимый ConnectionStrings:Timetable.");
        }
    }

    public NpgsqlDataSource CreateDataSource() => NpgsqlDataSource.Create(connectionString);

    public NpgsqlConnection CreateDedicatedConnection() => new(
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
}
