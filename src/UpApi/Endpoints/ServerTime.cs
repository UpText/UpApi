namespace UpApi.Endpoints;

public static class ServerTime
{
    public static IEndpointRouteBuilder MapServerTime(this IEndpointRouteBuilder app)
    {
        app.MapGet("/time", (ILoggerFactory loggerFactory) =>
            {
                var nowLocal = DateTimeOffset.Now;
                var nowUtc = nowLocal.ToUniversalTime();
                var logger = loggerFactory.CreateLogger("ServerTime");

                logger.LogInformation("ServerTime requested at {UtcNow}", nowUtc);

                return Results.Json(new ServerTimeResponse
                {
                    ServerTimeLocal = nowLocal.ToString("O"),
                    ServerTimeUtc = nowUtc.ToString("O"),
                    TimeZone = TimeZoneInfo.Local.Id
                });
            })
            .WithName("ServerTime")
            .WithTags("BuiltIn")
            .WithSummary("Return the current server time")
            .Produces<ServerTimeResponse>(StatusCodes.Status200OK, "application/json");

        return app;
    }

    public sealed class ServerTimeResponse
    {
        public string ServerTimeLocal { get; set; } = string.Empty;
        public string ServerTimeUtc { get; set; } = string.Empty;
        public string TimeZone { get; set; } = string.Empty;
    }
}
