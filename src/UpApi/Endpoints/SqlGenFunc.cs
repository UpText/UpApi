using System.Text.RegularExpressions;
using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using UpApi.Configuration;
using UpApi.Services;

namespace UpApi.Endpoints;

public static partial class SqlGenFunc
{
    public static IEndpointRouteBuilder MapSqlGenFunc(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/SqlGenerator", ["OPTIONS"], () => Results.NoContent())
            .ExcludeFromDescription();

        app.MapGet("/SqlGenerator", HandleLegacyAsync)
            .WithName("SqlGeneratorLegacyGet")
            .WithTags("BuiltIn")
            .WithSummary("Generate SQL stored procedure code for a table")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        app.MapPost("/SqlGenerator", HandleLegacyAsync)
            .WithName("SqlGeneratorLegacyPost")
            .WithTags("BuiltIn")
            .WithSummary("Generate SQL stored procedure code for a table")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        app.MapMethods("/{service}/sql-generator", ["OPTIONS"], () => Results.NoContent())
            .ExcludeFromDescription();

        app.MapGet("/{service}/sql-generator", HandleAsync)
            .WithName("SqlGeneratorRootGet")
            .WithTags("BuiltIn")
            .WithSummary("Generate SQL stored procedure code for a service table")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        app.MapPost("/{service}/sql-generator", HandleAsync)
            .WithName("SqlGeneratorRootPost")
            .WithTags("BuiltIn")
            .WithSummary("Generate SQL stored procedure code for a service table")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        app.MapMethods("/swa/{service}/sql-generator", ["OPTIONS"], () => Results.NoContent())
            .ExcludeFromDescription();

        app.MapGet("/swa/{service}/sql-generator", HandleAsync)
            .WithName("SqlGeneratorGet")
            .WithTags("BuiltIn")
            .WithSummary("Generate SQL stored procedure code for a service table")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        app.MapPost("/swa/{service}/sql-generator", HandleAsync)
            .WithName("SqlGeneratorPost")
            .WithTags("BuiltIn")
            .WithSummary("Generate SQL stored procedure code for a service table")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        return app;
    }

    private static Task<IResult> HandleLegacyAsync(
        [AsParameters] LegacySqlGenRequest parameters,
        HttpRequest request,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        return HandleCoreAsync(
            parameters.Service ?? "api",
            parameters.TableSchema ?? GetLegacySchema(request) ?? "dbo",
            parameters.Table ?? "categories",
            parameters.HttpVerb ?? "get",
            parameters.Paging ?? false,
            parameters.Sort ?? false,
            parameters.Search ?? false,
            serviceConfigResolver,
            loggerFactory,
            cancellationToken);
    }

    private static Task<IResult> HandleAsync(
        [AsParameters] SqlGenRequest parameters,
        HttpRequest request,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        return HandleCoreAsync(
            parameters.Service,
            parameters.TableSchema ?? GetLegacySchema(request) ?? "dbo",
            parameters.Table ?? "categories",
            parameters.HttpVerb ?? "get",
            parameters.Paging ?? false,
            parameters.Sort ?? false,
            parameters.Search ?? false,
            serviceConfigResolver,
            loggerFactory,
            cancellationToken);
    }

    private static Task<IResult> HandleCoreAsync(
        string service,
        string schema,
        string table,
        string verb,
        bool paging,
        bool sort,
        bool search,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SqlGenFunc");

        try
        {
            if (!serviceConfigResolver.TryGet(service, out var config))
            {
                return Task.FromResult<IResult>(
                    Results.Text("Service configuration was not found.", "text/plain", statusCode: StatusCodes.Status500InternalServerError));
            }

            if (string.IsNullOrWhiteSpace(config.SqlConnectionString))
            {
                return Task.FromResult<IResult>(
                    Results.Text("Missing database connectionString.", "text/plain", statusCode: StatusCodes.Status500InternalServerError));
            }

            if (string.IsNullOrWhiteSpace(config.SqlSchema))
            {
                return Task.FromResult<IResult>(
                    Results.Text("Missing SqlSchema configuration.", "text/plain", statusCode: StatusCodes.Status500InternalServerError));
            }

            if (!IsSafeSqlIdentifier(schema) || !IsSafeSqlIdentifier(table))
            {
                return Task.FromResult<IResult>(
                    Results.Text("Schema and table must be valid SQL identifiers.", "text/plain", statusCode: StatusCodes.Status400BadRequest));
            }

            if (!SqlProcCodeBuilder.IsSupportedVerb(verb))
            {
                return Task.FromResult<IResult>(
                    Results.Text("Verb must be one of GET, POST, PUT, DELETE, ALL.", "text/plain", statusCode: StatusCodes.Status400BadRequest));
            }

            var tableModel = SqlTableModelBuilder.ConstructTableModel(config.SqlConnectionString, schema, table, table, cancellationToken);
            var code = SqlProcCodeBuilder.BuildTableProc(config.SqlSchema, schema, tableModel, verb, search, paging, sort);

            return Task.FromResult<IResult>(Results.Text(code, "text/plain; charset=utf-8"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SQL generation failed for service {Service}", service);
            return Task.FromResult<IResult>(
                Results.Text("Failed to generate SQL.", "text/plain", statusCode: StatusCodes.Status500InternalServerError));
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SafeSqlIdentifierRegex();

    private static bool IsSafeSqlIdentifier(string value)
    {
        return SafeSqlIdentifierRegex().IsMatch(value);
    }

    private static string? GetLegacySchema(HttpRequest request)
    {
        var value = request.Query["schema"].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed class LegacySqlGenRequest
    {
        [DefaultValue("api")]
        public string? Service { get; init; } = "api";

        [DefaultValue("dbo")]
        [FromQuery(Name = "Table-schema")]
        public string? TableSchema { get; init; } = "dbo";

        public string? Table { get; init; }

        [FromQuery(Name = "http-verb")]
        public string? HttpVerb { get; init; }

        public bool? Paging { get; init; }

        public bool? Sort { get; init; }

        public bool? Search { get; init; }
    }

    public sealed class SqlGenRequest
    {
        [FromRoute]
        public string Service { get; init; } = string.Empty;

        [DefaultValue("dbo")]
        [FromQuery(Name = "Table-schema")]
        public string? TableSchema { get; init; } = "dbo";

        public string? Table { get; init; }

        [FromQuery(Name = "http-verb")]
        public string? HttpVerb { get; init; }

        public bool? Paging { get; init; }

        public bool? Sort { get; init; }

        public bool? Search { get; init; }
    }
}
