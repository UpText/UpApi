using System.Globalization;
using System.Text.Json;
using UpApi.Configuration;
using UpApi.Services;

namespace UpApi.Endpoints;

public static class SqlWebSiteDefinition
{
    public static IEndpointRouteBuilder MapSqlWebSiteDefinition(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/swa/SqlWebSiteDefinition", ["GET", "POST", "OPTIONS"], HandleLegacyAsync)
            .WithName("SqlWebSiteDefinitionLegacy")
            .WithTags("BuiltIn")
            .WithSummary("Return JSON describing the SQL-backed site");

        app.MapMethods("/{service}/site-definition", ["GET", "POST", "OPTIONS"], HandleAsync)
            .WithName("SqlWebSiteDefinitionRoot")
            .WithTags("BuiltIn")
            .WithSummary("Return JSON describing the SQL-backed site");

        app.MapMethods("/swa/{service}/site-definition", ["GET", "POST", "OPTIONS"], HandleAsync)
            .WithName("SqlWebSiteDefinition")
            .WithTags("BuiltIn")
            .WithSummary("Return JSON describing the SQL-backed site");

        return app;
    }

    private static Task<IResult> HandleLegacyAsync(
        HttpRequest request,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        string? service = null)
    {
        return HandleCoreAsync(service ?? "api", request, serviceConfigResolver, loggerFactory, cancellationToken);
    }

    private static Task<IResult> HandleAsync(
        string service,
        HttpRequest request,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        return HandleCoreAsync(service, request, serviceConfigResolver, loggerFactory, cancellationToken);
    }

    private static Task<IResult> HandleCoreAsync(
        string service,
        HttpRequest request,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return Task.FromResult<IResult>(Results.NoContent());
        }

        var logger = loggerFactory.CreateLogger("SqlWebSiteDefinition");
        logger.LogInformation("Site definition requested for service {Service}. Method={Method}", service, request.Method);

        try
        {
            if (!serviceConfigResolver.TryGet(service, out var config))
            {
                return Task.FromResult<IResult>(
                    Results.Text("Failed to generate site definition.", "text/plain", statusCode: StatusCodes.Status500InternalServerError));
            }

            if (string.IsNullOrWhiteSpace(config.SqlConnectionString))
            {
                throw new InvalidOperationException("Missing database connectionString");
            }

            var model = SqlOpenApiModelBuilder.ConstructModel(config.SqlConnectionString, config.SqlSchema);
            var document = BuildDocument(model);
            return Task.FromResult<IResult>(Results.Json(document));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Site definition generation failed for service {Service}", service);
            return Task.FromResult<IResult>(
                Results.Text("Failed to generate site definition.", "text/plain", statusCode: StatusCodes.Status500InternalServerError));
        }
    }

    private static object BuildDocument(SqlOpenApiModel model)
    {
        var resources = new List<object>();

        foreach (var controller in model.Controllers)
        {
            if (string.Equals(controller.Name, "login", StringComparison.Ordinal))
            {
                continue;
            }

            var postProc = controller.Procs.FirstOrDefault(proc => proc.Name.EndsWith("post", StringComparison.Ordinal));
            var putProc = controller.Procs.FirstOrDefault(proc => proc.Name.EndsWith("put", StringComparison.Ordinal));
            var deleteProc = controller.Procs.FirstOrDefault(proc => proc.Name.EndsWith("delete", StringComparison.Ordinal));
            var getProc = controller.Procs.FirstOrDefault(proc => proc.Name.EndsWith("get", StringComparison.Ordinal));

            var options = new
            {
                label = controller.Name,
                hasEdit = putProc is not null,
                hasDelete = deleteProc is not null,
                hasCreate = postProc is not null,
                hasPagination = controller.Columns.Any(column => string.Equals(column.Name, "total_rows", StringComparison.OrdinalIgnoreCase)),
                hasSearch = getProc?.Parameters.Any(param => string.Equals(param.Name, "@search", StringComparison.OrdinalIgnoreCase)) == true,
                hasSort = getProc?.Parameters.Any(param => string.Equals(param.Name, "@sort_field", StringComparison.OrdinalIgnoreCase)) == true
            };

            var fields = new List<object>();
            var visibleColumnIndex = 0;

            foreach (var column in controller.Columns)
            {
                if (string.Equals(column.Name, "total_rows", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var views = new List<string>();
                if (visibleColumnIndex++ < 30)
                {
                    views.Add("list");
                }

                if (postProc?.Parameters.Any(param => string.Equals(param.Name, "@" + column.Name, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    views.Add("create");
                }

                views.Add("show");

                if (putProc?.Parameters.Any(param => string.Equals(param.Name, "@" + column.Name, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    views.Add("edit");
                }

                var validators = column.IsNullable ? Array.Empty<string>() : ["required"];
                var (fieldType, reference) = GetFieldType(column);

                fields.Add(new
                {
                    source = column.Name,
                    type = fieldType,
                    view = views,
                    validators,
                    reference
                });
            }

            resources.Add(new
            {
                name = controller.Name,
                recordRepresentation = ToTitleCase(controller.Name),
                icon = "PostIcon",
                options,
                ui = ParseUi(controller.UiJson),
                fields
            });
        }

        return new
        {
            name = "SqlWebUI",
            resources
        };
    }

    private static object ParseUi(string? uiJson)
    {
        if (string.IsNullOrWhiteSpace(uiJson))
        {
            return new { };
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(uiJson);
        }
        catch (JsonException)
        {
            return new { };
        }
    }

    private static (string FieldType, string? Reference) GetFieldType(SqlOpenApiColumnInfo column)
    {
        if (column.Name.EndsWith("_url", StringComparison.OrdinalIgnoreCase))
        {
            return ("image", null);
        }

        if (column.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase))
        {
            return ("reference", column.Name[..column.Name.LastIndexOf("_id", StringComparison.OrdinalIgnoreCase)]);
        }

        return column.SqlType switch
        {
            "Int16" or "Int32" or "Int64" or "Decimal" or "Money" or "Double" or "Single" => ("number", null),
            "Boolean" => ("boolean", null),
            "DateTime" or "DateTime2" or "DateOnly" => ("date", null),
            _ => ("string", null)
        };
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }
}
