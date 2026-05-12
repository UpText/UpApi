using System.Net;
using Microsoft.Extensions.Options;
using UpApi.Configuration;

namespace UpApi.Endpoints;

public static class Home
{
    private const string BrandPrimary = "#082f49";
    private const string BrandCyan = "#67e8f9";
    private const string BrandAmber = "#f59e0b";
    private const string BrandInk = "#0f172a";
    private const string BrandMuted = "#475569";
    private const string BrandTeal = "#0f766e";

    public static IEndpointRouteBuilder MapHome(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", (IConfiguration configuration, IOptions<ServiceConfigurations> serviceConfigurations) =>
                Results.Content(RenderHomePage(configuration, serviceConfigurations.Value), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        return app;
    }

    private static string RenderHomePage(IConfiguration configuration, ServiceConfigurations serviceConfigurations)
    {
        var swaggerServices = GetSwaggerServices(configuration, serviceConfigurations);
        var swaggerLinks = swaggerServices.Count > 0
            ? string.Join(
                "\n",
                swaggerServices.Select(service =>
                    $"      <a class=\"service-link\" href=\"/docs/{Uri.EscapeDataString(service)}\">Swagger UI for {WebUtility.HtmlEncode(service)}</a>"))
            : "      <div class=\"service-link empty-state\">No Swagger services configured yet.</div>";

        return string.Join(
            "\n",
            "<!doctype html>",
            "<html lang=\"en\">",
            "<head>",
            "  <meta charset=\"utf-8\" />",
            "  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />",
            "  <link rel=\"preconnect\" href=\"https://fonts.googleapis.com\" />",
            "  <link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin />",
            "  <link href=\"https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=Space+Grotesk:wght@400;500;700&display=swap\" rel=\"stylesheet\" />",
            $"  <link rel=\"icon\" href=\"data:image/svg+xml,{Uri.EscapeDataString(RenderFaviconSvg())}\" />",
            "  <title>UpAPI</title>",
            "  <style>",
            "    :root { color-scheme: light; }",
            "    * { box-sizing: border-box; }",
            $"    body {{ margin: 0; font-family: 'IBM Plex Sans', sans-serif; color: {BrandInk}; background-image: radial-gradient(circle at top left, rgba(103, 232, 249, 0.22), transparent 30%), radial-gradient(circle at right 20%, rgba(245, 158, 11, 0.14), transparent 25%), linear-gradient(180deg, #f8fbfd 0%, #f5f7fb 100%); }}",
            "    .shell { min-height: 100vh; padding: 32px; }",
            "    .card { max-width: 1040px; margin: 0 auto; padding: 32px; border: 1px solid rgba(219, 228, 238, 0.9); border-radius: 32px; background: rgba(255,255,255,0.82); box-shadow: 0 20px 60px -35px rgba(15,23,42,0.45); backdrop-filter: blur(18px); }",
            "    .hero { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(280px, 0.8fr); gap: 28px; align-items: center; }",
            $"    .eyebrow {{ display: inline-block; padding: 8px 12px; border-radius: 999px; background: #ecfeff; color: {BrandTeal}; font-size: 12px; font-weight: 700; letter-spacing: 0.18em; text-transform: uppercase; }}",
            $"    h1 {{ margin: 18px 0 12px; font-family: 'Space Grotesk', sans-serif; font-size: clamp(42px, 7vw, 72px); line-height: 0.95; letter-spacing: -0.05em; color: {BrandInk}; }}",
            $"    .lede {{ margin: 0; max-width: 36rem; font-size: 18px; line-height: 1.7; color: {BrandMuted}; }}",
            "    .brand-panel { position: relative; overflow: hidden; padding: 24px; border-radius: 28px; background: linear-gradient(160deg, #082f49 0%, #0b3f61 100%); box-shadow: inset 0 1px 0 rgba(255,255,255,0.08); min-height: 100%; }",
            "    .brand-panel::before { content: ''; position: absolute; inset: -10% auto auto -12%; width: 190px; height: 190px; border-radius: 999px; background: rgba(103, 232, 249, 0.18); filter: blur(6px); }",
            "    .brand-panel::after { content: ''; position: absolute; right: -40px; bottom: -48px; width: 180px; height: 180px; border-radius: 999px; background: rgba(245, 158, 11, 0.18); filter: blur(2px); }",
            "    .brand-lockup { position: relative; z-index: 1; display: flex; gap: 18px; align-items: center; }",
            "    .brand-copy { position: relative; z-index: 1; margin-top: 22px; }",
            "    .brand-name { margin: 0; font-family: 'Space Grotesk', sans-serif; font-size: clamp(28px, 4vw, 42px); font-weight: 700; line-height: 1; letter-spacing: -0.04em; color: #f8fafc; }",
            "    .brand-tag { margin: 6px 0 0; font-size: 12px; font-weight: 700; letter-spacing: 0.28em; text-transform: uppercase; color: rgba(248,250,252,0.72); }",
            "    .brand-note { margin: 18px 0 0; font-size: 15px; line-height: 1.65; color: rgba(248,250,252,0.86); }",
            $"    .section-title {{ margin: 36px 0 14px; font-size: 13px; font-weight: 700; letter-spacing: 0.18em; text-transform: uppercase; color: {BrandTeal}; }}",
            "    .links { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }",
            "    .service-link, .utility-link { display: block; padding: 14px 16px; border-radius: 16px; text-decoration: none; color: #0f172a; background: rgba(255,255,255,0.92); border: 1px solid #dbe4ee; transition: transform 120ms ease, box-shadow 120ms ease, border-color 120ms ease; }",
            "    .service-link:hover, .utility-link:hover { transform: translateY(-1px); box-shadow: 0 14px 32px rgba(15,23,42,0.08); border-color: rgba(8,47,73,0.28); }",
            $"    .empty-state {{ color: {BrandMuted}; }}",
            "    .empty-state:hover { transform: none; box-shadow: none; border-color: #dbe4ee; }",
            "    .utility-row { display: flex; gap: 12px; flex-wrap: wrap; margin-top: 12px; }",
            "    .utility-link { width: fit-content; min-width: 140px; }",
            "    @media (max-width: 820px) { .shell { padding: 18px; } .card { padding: 22px; border-radius: 24px; } .hero { grid-template-columns: 1fr; } .brand-lockup { align-items: flex-start; } }",
            "  </style>",
            "</head>",
            "<body>",
            "  <main class=\"shell\">",
            "    <div class=\"card\">",
            "      <section class=\"hero\">",
            "        <div>",
            "          <span class=\"eyebrow\">UpText Platform</span>",
            "          <h1>UpAPI</h1>",
            "          <p class=\"lede\">SQL-backed endpoints, Swagger docs, JWT authentication.</p>",
            "        </div>",
            $"        {RenderBrandPanel()}",
            "      </section>",
            "      <div class=\"section-title\">Swagger Services</div>",
            $"      <div class=\"links\">{swaggerLinks}</div>",
            "      <div class=\"section-title\">Utilities</div>",
            "      <div class=\"utility-row\">",
            "        <a class=\"utility-link\" href=\"/ping\">Ping</a>",
            "        <a class=\"utility-link\" href=\"/live\">Liveness</a>",
            "        <a class=\"utility-link\" href=\"/health\">Readiness</a>",
            "        <a class=\"utility-link\" href=\"/health/details\">Health Details</a>",
            "        <a class=\"utility-link\" href=\"/docs\">Core Swagger UI</a>",
            "      </div>",
            "    </div>",
            "  </main>",
            "</body>",
            "</html>");
    }

    private static string RenderBrandPanel()
    {
        return $$"""
        <div class="brand-panel" aria-label="UpAPI brand panel">
          <div class="brand-lockup">
            {{RenderBrandIcon()}}
            <div>
              <p class="brand-name">UpText</p>
              <p class="brand-tag">SQL tools</p>
            </div>
          </div>
          <div class="brand-copy">
            <p class="brand-note">Open-source web tools for SQL developers and DBAs</p>
          </div>
        </div>
        """;
    }

    private static string RenderBrandIcon()
    {
        return $$"""
        <svg viewBox="0 0 48 48" role="img" aria-label="UpAPI icon" width="72" height="72">
          <rect x="3" y="3" width="42" height="42" rx="16" fill="{{BrandPrimary}}" />
          <path d="M14 29.5L24 12l10 17.5h-6L24 22l-4 7.5h-6Z" fill="{{BrandCyan}}" />
          <path d="M21.5 31h5v5h-5z" fill="{{BrandAmber}}" />
        </svg>
        """;
    }

    private static string RenderFaviconSvg()
    {
        return $$"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
          <rect x="3" y="3" width="42" height="42" rx="16" fill="{{BrandPrimary}}" />
          <path d="M14 29.5L24 12l10 17.5h-6L24 22l-4 7.5h-6Z" fill="{{BrandCyan}}" />
          <path d="M21.5 31h5v5h-5z" fill="{{BrandAmber}}" />
        </svg>
        """;
    }

    private static IReadOnlyList<string> GetSwaggerServices(
        IConfiguration configuration,
        ServiceConfigurations serviceConfigurations)
    {
        var swaggerServices = configuration["SQLWEBAPI__SWAGGER"];
        if (!string.IsNullOrWhiteSpace(swaggerServices))
        {
            return swaggerServices
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(service => !service.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                                  !service.Equals("false", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return serviceConfigurations.Services.Keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
