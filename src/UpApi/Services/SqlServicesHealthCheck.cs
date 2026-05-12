using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using UpApi.Configuration;

namespace UpApi.Services;

public sealed class SqlServicesHealthCheck(
    IOptions<ServiceConfigurations> serviceConfigurations) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var services = serviceConfigurations.Value.Services;
        var results = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        foreach (var (serviceName, serviceConfiguration) in services)
        {
            if (string.IsNullOrWhiteSpace(serviceConfiguration.SqlConnectionString))
            {
                failures.Add(serviceName);
                results[serviceName] = "Missing SQL connection string.";
                continue;
            }

            try
            {
                var builder = new SqlConnectionStringBuilder(serviceConfiguration.SqlConnectionString);
                builder.ConnectTimeout = Math.Min(Math.Max(builder.ConnectTimeout, 1), 5);

                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);

                results[serviceName] = "OK";
            }
            catch (Exception ex)
            {
                failures.Add(serviceName);
                results[serviceName] = ex.Message;
            }
        }

        if (failures.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                "One or more SQL services are unreachable.",
                data: results);
        }

        return HealthCheckResult.Healthy(
            "All SQL services responded successfully.",
            results);
    }
}
