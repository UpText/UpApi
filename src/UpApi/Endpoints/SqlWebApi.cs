using UpApi.Services;

namespace UpApi.Endpoints;

public static class SqlWebApi
{
    public static IEndpointRouteBuilder MapSqlWebApi(this IEndpointRouteBuilder app)
    {
        MapRoute(app, "/swa/{service}/{resource}", "SqlWebApi");
        MapRoute(app, "/swa/{service}/{resource}/{id}", "SqlWebApiWithId");

        return app;
    }

    private static void MapRoute(IEndpointRouteBuilder app, string pattern, string name)
    {
        app.MapMethods(pattern, ["GET", "POST", "PUT", "DELETE", "OPTIONS"], HandleAsync)
            .WithName(name)
            .WithTags("SqlWebApi")
            .WithSummary("Execute SQL-backed REST endpoints");
    }

    private static Task<IResult> HandleAsync(
        string service,
        string resource,
        HttpRequest request,
        ISqlWebApiExecutor executor,
        CancellationToken cancellationToken,
        string? id = null,
        string? details = null)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return Task.FromResult<IResult>(new SqlWebApiHttpResult(
                statusCode: StatusCodes.Status204NoContent,
                headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Allow"] = "GET, POST, PUT, DELETE, OPTIONS"
                }));
        }

        return executor.ExecuteAsync(request, service, resource, id, details, cancellationToken);
    }
}
