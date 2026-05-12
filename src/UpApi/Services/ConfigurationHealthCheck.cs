using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using UpApi.Configuration;

namespace UpApi.Services;

public sealed class ConfigurationHealthCheck(
    IOptions<ServiceConfigurations> serviceConfigurations,
    IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var services = serviceConfigurations.Value.Services;
        if (services.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "No configured services were found.",
                data: new Dictionary<string, object>
                {
                    ["serviceCount"] = 0
                }));
        }

        var invalidServices = services
            .Where(service =>
                string.IsNullOrWhiteSpace(service.Value.SqlSchema) ||
                string.IsNullOrWhiteSpace(service.Value.SqlConnectionString))
            .Select(service => service.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missingJwtSettings = new[]
        {
            "JWT_SECRET",
            "JWT_ISSUER",
            "JWT_AUDIENCE"
        }
        .Where(key => string.IsNullOrWhiteSpace(GetSetting(configuration, key)))
        .ToArray();

        var data = new Dictionary<string, object>
        {
            ["serviceCount"] = services.Count,
            ["services"] = services.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            ["invalidServices"] = invalidServices,
            ["missingJwtSettings"] = missingJwtSettings
        };

        if (invalidServices.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "One or more services are missing required SQL configuration.",
                data: data));
        }

        if (missingJwtSettings.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "JWT settings are incomplete. Protected endpoints may fail.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Configuration is valid.",
            data));
    }

    private static string? GetSetting(IConfiguration configuration, string key)
    {
        return configuration[key] ?? Environment.GetEnvironmentVariable(key);
    }
}
