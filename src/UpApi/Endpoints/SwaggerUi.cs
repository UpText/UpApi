using System.Net;

namespace UpApi.Endpoints;

public static class SwaggerUi
{
    private const string DefaultDocumentName = "swagger";
    private const string DefaultSwaggerUrl = $"/{DefaultDocumentName}.json";

    public static IEndpointRouteBuilder MapSwaggerUi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/docs", () => Results.Content(RenderSwaggerUiPage(null), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        app.MapGet("/docs/{service}", (string service) => Results.Content(RenderSwaggerUiPage(service), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        return app;
    }

    private static string RenderSwaggerUiPage(string? service)
    {
        var swaggerUrl = string.IsNullOrWhiteSpace(service)
            ? DefaultSwaggerUrl
            : $"/{Uri.EscapeDataString(service)}/swagger.json";
        var pageTitle = string.IsNullOrWhiteSpace(service)
            ? "UpApi Swagger"
            : $"UpApi Swagger - {WebUtility.HtmlEncode(service)}";
        var headerText = string.IsNullOrWhiteSpace(service)
            ? $"Swagger loading <code>{WebUtility.HtmlEncode(swaggerUrl)}</code>"
            : $"Swagger for <code>{WebUtility.HtmlEncode(service)}</code> loading <code>{WebUtility.HtmlEncode(swaggerUrl)}</code>";

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{pageTitle}}</title>
  <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
  <style>
    html { box-sizing: border-box; overflow-y: scroll; }
    *, *:before, *:after { box-sizing: inherit; }
    body { margin: 0; background: #faf7f1; }
    .topbar {
      padding: 12px 20px;
      border-bottom: 1px solid #ddd4c8;
      background: #f3ecdf;
      font-family: system-ui, sans-serif;
      display: flex;
      align-items: center;
      gap: 14px;
      flex-wrap: wrap;
    }
    .topbar-brand {
      font-size: 1.55rem;
      line-height: 1;
      font-weight: 800;
      color: #4d2d14;
      text-decoration: none;
      letter-spacing: 0.01em;
    }
    .topbar-brand:hover {
      text-decoration: underline;
    }
    .topbar-text {
      font-size: 1rem;
      color: #5c4636;
    }
    .topbar code {
      padding: 2px 6px;
      border-radius: 6px;
      background: #fffaf2;
    }
  </style>
</head>
<body>
  <div class="topbar">
    <a class="topbar-brand" href="/">UpApi</a>
    <div class="topbar-text">{{headerText}}</div>
  </div>
  <div id="swagger-ui"></div>
  <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js" crossorigin></script>
  <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-standalone-preset.js" crossorigin></script>
  <script>
    window.onload = function () {
      window.ui = SwaggerUIBundle({
        url: {{System.Text.Json.JsonSerializer.Serialize(swaggerUrl)}},
        dom_id: '#swagger-ui',
        deepLinking: true,
        presets: [
          SwaggerUIBundle.presets.apis,
          SwaggerUIStandalonePreset
        ],
        layout: "StandaloneLayout"
      });
    };
  </script>
</body>
</html>
""";
    }
}
