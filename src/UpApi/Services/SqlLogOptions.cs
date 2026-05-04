namespace UpApi.Services;

public sealed class SqlLogOptions
{
    public const string SectionName = "SqlLog";

    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string TableName { get; set; } = "log";
}
