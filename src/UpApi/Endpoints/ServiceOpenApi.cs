using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.OpenApi;
using System.Net.Http;
using System.Text.Json.Nodes;
using UpApi.Configuration;
using UpApi.Services;

namespace UpApi.Endpoints;

public static class ServiceOpenApi
{
    public static IEndpointRouteBuilder MapServiceOpenApi(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/swa/{service}/swagger.json", ["GET", "POST", "OPTIONS"], HandleAsync)
            .WithName("ServiceOpenApi")
            .WithTags("OpenApi")
            .WithSummary("Generate a service-specific OpenAPI document")
            .ExcludeFromDescription();

        return app;
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

    private static async Task<IResult> HandleCoreAsync(
        string service,
        HttpRequest request,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ServiceOpenApi");
        logger.LogInformation("OpenApi requested for service {Service}. Method={Method} Url={Url}", service, request.Method, request.GetDisplayUrl());

        try
        {
            if (!serviceConfigResolver.TryGet(service, out var config))
            {
                return Results.Text("Failed to generate OpenAPI document.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
            }

            if (string.IsNullOrWhiteSpace(config.SqlConnectionString))
            {
                throw new InvalidOperationException("Missing database connectionString");
            }

            var model = SqlOpenApiModelBuilder.ConstructModel(config.SqlConnectionString, config.SqlSchema);
            var document = BuildDocument(request, service, model);
            var output = await document.SerializeAsync(OpenApiSpecVersion.OpenApi3_0, "json", cancellationToken);
            output = PatchSecurityMetadata(output, model);
            logger.LogInformation("OpenApi document generated successfully for service {Service}", service);
            return Results.Text(output, "application/json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenApi generation failed for service {Service}", service);
            return Results.Text("Failed to generate OpenAPI document.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static OpenApiDocument BuildDocument(HttpRequest request, string service, SqlOpenApiModel model)
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Version = "1.0.0",
                Title = $"OpenApi document generated for service: {service} by SqlWebApi "
            },
            Servers = new List<OpenApiServer>
            {
                new() { Url = GetServerUrl(request, service) }
            },
            Paths = new OpenApiPaths()
        };

        var hasProtectedOperations = model.Controllers
            .SelectMany(controller => controller.Procs)
            .Any(HasAuthParameter);

        if (hasProtectedOperations)
        {
            document.AddJwtBearer(
                schemeName: "Bearer",
                alsoApplyToOperations: false,
                description: "Enter: Bearer {token}",
                applyGlobally: true);
        }

        foreach (var controller in model.Controllers)
        {
            var operations = new Dictionary<HttpMethod, OpenApiOperation>();
            foreach (var proc in controller.Procs)
            {
                var operation = new OpenApiOperation
                {
                    Responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse { Description = "OK" }
                    },
                    Parameters = []
                };

                if (HasAuthParameter(proc))
                {
                    MarkOperationAsLocked(operation);
                }

                foreach (var update in controller.OpenApiUpdates)
                {
                    if (UpdateAppliesToProc(update, proc))
                    {
                        if (string.Equals(update.ClassName, "response", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(update.Property, "description", StringComparison.OrdinalIgnoreCase))
                        {
                            operation.Responses[update.Name] = new OpenApiResponse { Description = update.Value };
                        }
                    }
                }

                foreach (var param in proc.Parameters)
                {
                    if (param.Name.StartsWith("@auth_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var openApiParameter = new OpenApiParameter
                    {
                        Name = param.Name.StartsWith('@') ? param.Name[1..] : param.Name,
                        In = ParameterLocation.Query,
                        Schema = CreateParameterSchema(param)
                    };

                    foreach (var update in controller.OpenApiUpdates)
                    {
                        if (UpdateAppliesToProc(update, proc) &&
                            string.Equals(update.ClassName, "parameter", StringComparison.OrdinalIgnoreCase) &&
                            openApiParameter.Name == update.Name &&
                            string.Equals(update.Property, "description", StringComparison.OrdinalIgnoreCase))
                        {
                            openApiParameter.Description = update.Value;
                        }
                    }

                    if (UsesRequestBody(proc))
                    {
                        AddRequestBodyProperty(operation, openApiParameter, param);
                    }
                    else
                    {
                        operation.Parameters.Add(openApiParameter);
                    }
                }

                foreach (var update in controller.OpenApiUpdates)
                {
                    if (!UpdateAppliesToProc(update, proc))
                    {
                        continue;
                    }

                    if (string.Equals(update.ClassName, "operation", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(update.Property, "description", StringComparison.OrdinalIgnoreCase))
                        {
                            operation.Description = update.Value;
                        }
                        else if (string.Equals(update.Property, "summary", StringComparison.OrdinalIgnoreCase))
                        {
                            operation.Summary = HasAuthParameter(proc) ? PrefixLockSymbol(update.Value) : update.Value;
                        }
                        else if (string.Equals(update.Property, "tag", StringComparison.OrdinalIgnoreCase))
                        {
                            AddOperationTag(document, operation, update);
                        }
                    }
                }

                var verb = proc.GetVerb();
                if (verb is not null)
                {
                    operations[verb] = operation;
                }
            }

            document.Paths.Add("/" + controller.Name, new OpenApiPathItem { Operations = operations });
        }
        return document;
    }

    private static string PatchSecurityMetadata(string json, SqlOpenApiModel model)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        if (root is null)
        {
            return json;
        }

        var hasProtectedOperations = false;
        var paths = root["paths"]?.AsObject();
        if (paths is null)
        {
            return json;
        }

        foreach (var controller in model.Controllers)
        {
            if (paths["/" + controller.Name] is not JsonObject pathObject)
            {
                continue;
            }

            foreach (var proc in controller.Procs)
            {
                var verb = proc.GetVerb();
                if (verb is null)
                {
                    continue;
                }

                var verbName = verb.Method.ToLowerInvariant();
                if (pathObject[verbName] is not JsonObject operationObject)
                {
                    continue;
                }

                PatchOperationMetadata(operationObject, proc);

                if (HasAuthParameter(proc))
                {
                    operationObject["security"] = CreateBearerSecurityArray();
                    hasProtectedOperations = true;
                }
            }
        }

        if (!hasProtectedOperations)
        {
            root.Remove("security");
            return root.ToJsonString();
        }

        root["security"] = CreateBearerSecurityArray();
        return root.ToJsonString();
    }

    private static void PatchOperationMetadata(JsonObject operationObject, SqlOpenApiProcInfo proc)
    {
        if (UsesRequestBody(proc))
        {
            PatchRequestBodyMetadata(operationObject, proc);
            operationObject.Remove("parameters");
            return;
        }

        PatchParameterMetadata(operationObject, proc);
    }

    private static void PatchRequestBodyMetadata(JsonObject operationObject, SqlOpenApiProcInfo proc)
    {
        var parameterDescriptions = ReadParameterDescriptions(operationObject);
        var properties = new JsonObject();

        foreach (var param in proc.Parameters)
        {
            if (param.Name.StartsWith("@auth_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertyName = NormalizeParameterName(param.Name);
            properties[propertyName] = CreateJsonParameterSchema(
                param,
                parameterDescriptions.TryGetValue(propertyName, out var description) ? description : null);
        }

        if (properties.Count == 0)
        {
            return;
        }

        operationObject["requestBody"] = new JsonObject
        {
            ["content"] = new JsonObject
            {
                ["application/json"] = new JsonObject
                {
                    ["schema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties
                    }
                }
            }
        };
    }

    private static void PatchParameterMetadata(JsonObject operationObject, SqlOpenApiProcInfo proc)
    {
        if (operationObject["parameters"] is not JsonArray parameters)
        {
            return;
        }

        foreach (var parameterNode in parameters)
        {
            if (parameterNode is not JsonObject parameterObject)
            {
                continue;
            }

            var parameterName = parameterObject["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                continue;
            }

            var parameterInfo = proc.Parameters.FirstOrDefault(param =>
                string.Equals(NormalizeParameterName(param.Name), parameterName, StringComparison.OrdinalIgnoreCase));

            if (parameterInfo is null)
            {
                continue;
            }

            parameterObject["schema"] = CreateJsonParameterSchema(parameterInfo);
        }
    }

    private static JsonObject CreateJsonParameterSchema(SqlOpenApiParameterInfo param)
    {
        var schema = new JsonObject();
        ApplySchemaMetadata(schema, param);
        return schema;
    }

    private static JsonObject CreateJsonParameterSchema(SqlOpenApiParameterInfo param, string? description)
    {
        var schema = CreateJsonParameterSchema(param);
        if (!string.IsNullOrWhiteSpace(description))
        {
            schema["description"] = description;
        }

        return schema;
    }

    private static Dictionary<string, string> ReadParameterDescriptions(JsonObject operationObject)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (operationObject["parameters"] is not JsonArray parameters)
        {
            return descriptions;
        }

        foreach (var parameterNode in parameters)
        {
            if (parameterNode is not JsonObject parameterObject)
            {
                continue;
            }

            var parameterName = parameterObject["name"]?.GetValue<string>();
            var description = parameterObject["description"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(parameterName) || string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            descriptions[parameterName] = description;
        }

        return descriptions;
    }

    private static string NormalizeParameterName(string name)
    {
        return name.StartsWith('@') ? name[1..] : name;
    }

    private static bool UsesRequestBody(SqlOpenApiProcInfo proc)
    {
        var verb = proc.GetVerb();
        return verb == HttpMethod.Post || verb == HttpMethod.Put;
    }

    private static void AddRequestBodyProperty(OpenApiOperation operation, OpenApiParameter openApiParameter, SqlOpenApiParameterInfo param)
    {
        operation.RequestBody ??= new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                    }
                }
            }
        };

        var schema = operation.RequestBody.Content["application/json"].Schema as OpenApiSchema;
        if (schema is null)
        {
            return;
        }

        if (schema.Properties is null)
        {
            return;
        }

        schema.Properties[openApiParameter.Name] = CreateParameterSchema(param, openApiParameter.Description);
    }

    private static void ApplySchemaMetadata(OpenApiSchema schema, SqlOpenApiParameterInfo param)
    {
        var parameterName = NormalizeParameterName(param.Name);
        var sqlType = param.SqlType.ToLowerInvariant();

        switch (sqlType)
        {
            case "bigint":
                schema.Type = JsonSchemaType.Integer;
                schema.Format = "int64";
                break;
            case "int":
            case "smallint":
            case "tinyint":
                schema.Type = JsonSchemaType.Integer;
                schema.Format = "int32";
                break;
            case "bit":
                schema.Type = JsonSchemaType.Boolean;
                break;
            case "decimal":
            case "numeric":
            case "money":
            case "smallmoney":
                schema.Type = JsonSchemaType.Number;
                break;
            case "float":
                schema.Type = JsonSchemaType.Number;
                schema.Format = "double";
                break;
            case "real":
                schema.Type = JsonSchemaType.Number;
                schema.Format = "float";
                break;
            case "date":
                schema.Type = JsonSchemaType.String;
                schema.Format = "date";
                break;
            case "datetime":
            case "datetime2":
            case "smalldatetime":
            case "datetimeoffset":
                schema.Type = JsonSchemaType.String;
                schema.Format = "date-time";
                break;
            case "uniqueidentifier":
                schema.Type = JsonSchemaType.String;
                schema.Format = "uuid";
                break;
            case "binary":
            case "varbinary":
            case "image":
            case "timestamp":
            case "rowversion":
                schema.Type = JsonSchemaType.String;
                schema.Format = "byte";
                break;
            default:
                schema.Type = JsonSchemaType.String;
                break;
        }

        if (string.Equals(parameterName, "password", StringComparison.OrdinalIgnoreCase))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "password";
        }
    }

    private static void ApplySchemaMetadata(JsonObject schema, SqlOpenApiParameterInfo param)
    {
        var openApiSchema = new OpenApiSchema();
        ApplySchemaMetadata(openApiSchema, param);

        if (openApiSchema.Type.HasValue)
        {
            schema["type"] = openApiSchema.Type.Value switch
            {
                JsonSchemaType.Integer => "integer",
                JsonSchemaType.Number => "number",
                JsonSchemaType.Boolean => "boolean",
                _ => "string"
            };
        }

        if (!string.IsNullOrWhiteSpace(openApiSchema.Format))
        {
            schema["format"] = openApiSchema.Format;
        }
    }

    private static JsonArray CreateBearerSecurityArray()
    {
        return
        [
            new JsonObject
            {
                ["Bearer"] = new JsonArray()
            }
        ];
    }

    private static bool HasAuthParameter(SqlOpenApiProcInfo proc)
    {
        return proc.Parameters.Any(param => param.Name.StartsWith("@auth_", StringComparison.OrdinalIgnoreCase));
    }

    private static bool UpdateAppliesToProc(SqlOpenApiUpdate update, SqlOpenApiProcInfo proc)
    {
        if (update.Operation == "*")
        {
            return true;
        }

        return proc.Name.EndsWith(update.Operation, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddOperationTag(OpenApiDocument document, OpenApiOperation operation, SqlOpenApiUpdate update)
    {
        var tagName = string.IsNullOrWhiteSpace(update.Value) ? update.Name : update.Value;
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return;
        }

        document.Tags ??= new HashSet<OpenApiTag>();
        if (!document.Tags.Any(tag => string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase)))
        {
            document.Tags.Add(new OpenApiTag { Name = tagName });
        }

        operation.Tags ??= new HashSet<OpenApiTagReference>();
        if (operation.Tags.Any(tag => string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Tags.Add(new OpenApiTagReference(tagName, document, null));
    }

    private static OpenApiSchema CreateParameterSchema(SqlOpenApiParameterInfo param, string? description = null)
    {
        var schema = new OpenApiSchema();
        ApplySchemaMetadata(schema, param);
        if (!string.IsNullOrWhiteSpace(description))
        {
            schema.Description = description;
        }
        return schema;
    }

    private static void MarkOperationAsLocked(OpenApiOperation operation)
    {
        operation.Summary = PrefixLockSymbol(operation.Summary);
    }

    private static string PrefixLockSymbol(string? value)
    {
        const string lockSymbol = "\U0001F512";
        if (string.IsNullOrWhiteSpace(value))
        {
            return lockSymbol;
        }

        return value.StartsWith(lockSymbol, StringComparison.Ordinal) ? value : $"{lockSymbol} {value}";
    }

    private static string GetServerUrl(HttpRequest request, string service)
    {
        var configuredBaseUrl = GetSetting(request.HttpContext.RequestServices, "OPENAPI_PUBLIC_BASEURL");
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return $"{configuredBaseUrl.TrimEnd('/')}/swa/{Uri.EscapeDataString(service)}";
        }

        var forwardedProto = GetFirstHeaderValue(request, "X-Forwarded-Proto", "X-Original-Proto");
        var forwardedHost = GetFirstHeaderValue(request, "X-Forwarded-Host", "X-Original-Host", "Host");
        var forwardedPrefix = GetFirstHeaderValue(request, "X-Forwarded-Prefix", "X-Original-PathBase");

        var builder = new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? -1);
        if (!string.IsNullOrWhiteSpace(forwardedProto))
        {
            builder.Scheme = forwardedProto;
        }

        if (!string.IsNullOrWhiteSpace(forwardedHost))
        {
            if (forwardedHost.Contains(':', StringComparison.Ordinal))
            {
                var parts = forwardedHost.Split(':', 2);
                builder.Host = parts[0];
                if (int.TryParse(parts[1], out var port))
                {
                    builder.Port = port;
                }
            }
            else
            {
                builder.Host = forwardedHost;
                builder.Port = builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
            }
        }

        var pathBase = string.IsNullOrWhiteSpace(forwardedPrefix) ? string.Empty : "/" + forwardedPrefix.Trim('/');
        builder.Path = $"{pathBase}/swa/{Uri.EscapeDataString(service)}".Replace("//", "/");
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;

        if ((builder.Scheme == "https" && builder.Port == 443) || (builder.Scheme == "http" && builder.Port == 80))
        {
            builder.Port = -1;
        }

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string? GetFirstHeaderValue(HttpRequest request, params string[] names)
    {
        foreach (var name in names)
        {
            if (request.Headers.TryGetValue(name, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Split(',')[0].Trim();
                }
            }
        }

        return null;
    }

    private static string? GetSetting(IServiceProvider services, string key)
    {
        var configuration = services.GetService<IConfiguration>();
        return configuration?[key] ?? Environment.GetEnvironmentVariable(key);
    }
}
