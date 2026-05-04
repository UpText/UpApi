using Microsoft.OpenApi;

namespace UpApi.Services;

public static class OpenApiDocumentJwtExtensions
{
    public static OpenApiSecurityRequirement CreateJwtBearerRequirement(
        this OpenApiDocument doc,
        string schemeName = "Bearer")
    {
        ArgumentNullException.ThrowIfNull(doc);

        return new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(schemeName, doc, null)] = []
        };
    }

    public static void AddJwtBearer(
        this OpenApiDocument doc,
        string schemeName = "Bearer",
        bool alsoApplyToOperations = false,
        string? description = "Enter: Bearer {token}",
        bool applyGlobally = true)
    {
        ArgumentNullException.ThrowIfNull(doc);

        doc.Components ??= new OpenApiComponents();
        doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        doc.Components.SecuritySchemes[schemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = description
        };

        var requirement = doc.CreateJwtBearerRequirement(schemeName);

        if (applyGlobally)
        {
            doc.Security ??= [];
            doc.Security.Add(requirement);
        }

        if (!alsoApplyToOperations)
        {
            return;
        }

        foreach (var path in doc.Paths.Values)
        {
            if (path.Operations is null)
            {
                continue;
            }

            foreach (var operation in path.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(requirement);
            }
        }
    }

    public static void RequireJwtBearer(
        this OpenApiOperation operation,
        OpenApiDocument doc,
        string schemeName = "Bearer")
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(doc);

        operation.Security ??= [];
        operation.Security.Add(doc.CreateJwtBearerRequirement(schemeName));
    }
}
