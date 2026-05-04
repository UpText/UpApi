using UpApi.Endpoints;
using UpApi.Configuration;
using UpApi.Services;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi("swagger", options =>
{
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var relativePath = context.Description.RelativePath;
        if (!string.Equals(relativePath, "SqlGenerator", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(relativePath, "swa/{service}/sql-generator", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var httpVerbParameter = operation.Parameters?.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "http-verb", StringComparison.Ordinal));
        if (httpVerbParameter?.Schema is not OpenApiSchema schema)
        {
            return Task.CompletedTask;
        }

        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Enum =
        [
            JsonValue.Create("all"),
            JsonValue.Create("get"),
            JsonValue.Create("put"),
            JsonValue.Create("post"),
            JsonValue.Create("delete")
        ];

        return Task.CompletedTask;
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddMemoryCache();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://localhost:5173",
                "http://127.0.0.1:5173",
                "https://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Range");
    });
});
builder.Services.Configure<ServiceConfigurations>(options =>
    builder.Configuration.GetSection(ServiceConfigurations.SectionName).Bind(options.Services));
builder.Services.Configure<SqlLogOptions>(options =>
{
    builder.Configuration.GetSection(SqlLogOptions.SectionName).Bind(options);
    options.ConnectionString = options.ConnectionString.Trim();
    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        options.ConnectionString = builder.Configuration["SqlServerLogDb"]?.Trim() ?? string.Empty;
    }
});
builder.Services.AddSingleton<IServiceConfigResolver, ServiceConfigResolver>();
builder.Services.AddSingleton<ISqlWebApiExecutor, SqlWebApiExecutor>();
builder.Services.AddSingleton<ISqlLog, SqlLog>();

var app = builder.Build();
await app.Services.GetRequiredService<ISqlLog>().EnsureTableAsync();

app.MapOpenApi("/{documentName}.json");

if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

if (isDevelopment)
{
    app.UseCors("LocalFrontend");
}

app.MapPing();
app.MapHome();
app.MapServerTime();
app.MapSqlGenFunc();
app.MapSqlImageFunc();
app.MapSqlWebApi();
app.MapSqlWebSiteDefinition();
app.MapServiceOpenApi();
app.MapSwaggerUi();

app.Run();
