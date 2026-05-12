using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UpApi.Endpoints;

public static class Health
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteLivenessResponseAsync
        })
        .WithName("Live")
        .WithTags("BuiltIn")
        .WithSummary("Return process liveness");

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready", StringComparer.OrdinalIgnoreCase),
            ResponseWriter = WriteReadinessResponseAsync
        })
        .WithName("Health")
        .WithTags("BuiltIn")
        .WithSummary("Return public readiness status");

        app.MapHealthChecks("/health/details", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready", StringComparer.OrdinalIgnoreCase),
            ResponseWriter = WriteDetailedResponseAsync
        })
        .WithName("HealthDetails")
        .WithTags("BuiltIn")
        .WithSummary("Return detailed readiness diagnostics");

        return app;
    }

    private static Task WriteLivenessResponseAsync(HttpContext context, HealthReport report)
    {
        return WriteJsonAsync(context, report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = report.Status.ToString().ToLowerInvariant(),
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private static Task WriteReadinessResponseAsync(HttpContext context, HealthReport report)
    {
        return WriteJsonAsync(context, report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = report.Status.ToString().ToLowerInvariant(),
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private static Task WriteDetailedResponseAsync(HttpContext context, HealthReport report)
    {
        return WriteJsonAsync(context, report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = report.Status.ToString().ToLowerInvariant(),
                totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
                timestamp = DateTimeOffset.UtcNow,
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString().ToLowerInvariant(),
                        description = entry.Value.Description,
                        durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                        data = entry.Value.Data.ToDictionary(
                            item => item.Key,
                            item => item.Value)
                    },
                    StringComparer.OrdinalIgnoreCase)
            });
    }

    private static Task WriteJsonAsync(HttpContext context, int statusCode, object payload)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
