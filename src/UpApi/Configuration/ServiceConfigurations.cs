namespace UpApi.Configuration;

public sealed class ServiceConfigurations
{
    public const string SectionName = "Services";

    public Dictionary<string, ServiceConfiguration> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ServiceConfiguration
{
    public string SqlSchema { get; set; } = string.Empty;
    public string SqlConnectionString { get; set; } = string.Empty;
}
