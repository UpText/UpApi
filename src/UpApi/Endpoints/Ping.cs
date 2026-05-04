namespace UpApi.Endpoints;

public static class Ping
{
    public static IEndpointRouteBuilder MapPing(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/ping", ["GET", "POST"], (ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("HttpPing");
                logger.LogInformation("Ping function processed a request.");

                return Results.Text("pong", "text/plain; charset=utf-8");
            })
            .WithName("Ping")
            .WithTags("BuiltIn")
            .WithSummary("Return pong")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain");

        return app;
    }
}
