using System.Net;
using Microsoft.Extensions.Options;
using UpApi.Configuration;

namespace UpApi.Endpoints;

public static class Home
{
    private const string AccentColor = "#f5b301";
    private const string AccentColorSoft = "#ffd34d";
    private const string InkColor = "#0d1117";

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
        var logos = RenderLogos();

        return string.Join(
            "\n",
            "<!doctype html>",
            "<html lang=\"en\">",
            "<head>",
            "  <meta charset=\"utf-8\" />",
            "  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />",
            "  <title>UpText API</title>",
            "  <style>",
            "    :root { color-scheme: light; }",
            "    * { box-sizing: border-box; }",
            "    body { margin: 0; font-family: system-ui, sans-serif; background: radial-gradient(circle at top left, #fff7d8 0, #f7efe2 38%, #f2ebe2 100%); color: #111827; }",
            "    .shell { min-height: 100vh; padding: 32px; }",
            "    .card { max-width: 980px; margin: 0 auto; padding: 32px; border: 1px solid #e5dac7; border-radius: 28px; background: rgba(255,255,255,0.86); box-shadow: 0 22px 60px rgba(28, 22, 10, 0.12); backdrop-filter: blur(8px); }",
            "    .hero { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(280px, 0.8fr); gap: 28px; align-items: center; }",
            "    .eyebrow { display: inline-block; padding: 8px 12px; border-radius: 999px; background: #fff5cc; color: #8a5a00; font-size: 12px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }",
            "    h1 { margin: 18px 0 12px; font-size: clamp(42px, 7vw, 72px); line-height: 0.95; letter-spacing: -0.05em; }",
            "    .lede { margin: 0; max-width: 36rem; font-size: 18px; line-height: 1.6; color: #5f5a52; }",
            "    .logo-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }",
            "    .logo-card { padding: 16px; border-radius: 22px; background: linear-gradient(180deg, #171c25 0%, #10151d 100%); box-shadow: inset 0 1px 0 rgba(255,255,255,0.08); }",
            "    .logo-card.light { background: linear-gradient(180deg, #fff8dd 0%, #ffe7a6 100%); }",
            "    .logo-card.wide { grid-column: 1 / -1; }",
            "    .logo-label { display: block; margin-top: 10px; font-size: 12px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: #f7d56f; }",
            "    .logo-card.light .logo-label { color: #7d5400; }",
            "    .section-title { margin: 36px 0 14px; font-size: 13px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: #8a847a; }",
            "    .links { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }",
            "    .service-link, .utility-link { display: block; padding: 14px 16px; border-radius: 16px; text-decoration: none; color: #161616; background: #fffdfa; border: 1px solid #e8dcc9; transition: transform 120ms ease, box-shadow 120ms ease, border-color 120ms ease; }",
            "    .service-link:hover, .utility-link:hover { transform: translateY(-1px); box-shadow: 0 10px 24px rgba(30, 24, 13, 0.08); border-color: #d7ba54; }",
            "    .empty-state { color: #70685d; }",
            "    .empty-state:hover { transform: none; box-shadow: none; border-color: #e8dcc9; }",
            "    .utility-row { display: flex; gap: 12px; flex-wrap: wrap; margin-top: 12px; }",
            "    .utility-link { width: fit-content; min-width: 140px; }",
            "    @media (max-width: 820px) { .shell { padding: 18px; } .card { padding: 22px; border-radius: 22px; } .hero { grid-template-columns: 1fr; } }",
            "  </style>",
            "</head>",
            "<body>",
            "  <main class=\"shell\">",
            "    <div class=\"card\">",
            "      <section class=\"hero\">",
            "        <div>",
            "          <span class=\"eyebrow\">UpText Platform</span>",
            "          <h1>UpText API</h1>",
            "          <p class=\"lede\">SQL-backed endpoints, Swagger surfaces, and internal service docs with branding that matches the rest of UpText.</p>",
            "        </div>",
            logos,
            "      </section>",
            "      <div class=\"section-title\">Swagger Services</div>",
            $"      <div class=\"links\">{swaggerLinks}</div>",
            "      <div class=\"section-title\">Utilities</div>",
            "      <div class=\"utility-row\">",
            "        <a class=\"utility-link\" href=\"/ping\">Health</a>",
            "        <a class=\"utility-link\" href=\"/docs\">Core Swagger UI</a>",
            "      </div>",
            "    </div>",
            "  </main>",
            "</body>",
            "</html>");
    }

    private static string RenderLogos()
    {
        return $$"""
        <div class="logo-grid" aria-label="UpText logo studies">
          <div class="logo-card wide">
            {{RenderWordmarkLogo()}}
            <span class="logo-label">Wordmark lockup</span>
          </div>
          <div class="logo-card">
            {{RenderMarkLogo(AccentColor, InkColor)}}
            <span class="logo-label">App mark</span>
          </div>
          <div class="logo-card light">
            {{RenderOutlinedLogo()}}
            <span class="logo-label">Signal mark</span>
          </div>
        </div>
        """;
    }

    private static string RenderWordmarkLogo()
    {
        return $$"""
        <svg viewBox="0 0 440 120" role="img" aria-label="UpText wordmark" width="100%" height="auto">
          <rect x="0" y="0" width="440" height="120" rx="24" fill="transparent" />
          <g transform="translate(18 18)">
            <rect x="0" y="0" width="34" height="34" rx="9" fill="{{AccentColor}}" />
            <rect x="46" y="0" width="34" height="22" rx="8" fill="#ffffff" />
            <rect x="0" y="46" width="28" height="28" rx="8" fill="#ffffff" />
            <rect x="40" y="40" width="40" height="40" rx="10" fill="{{AccentColorSoft}}" />
          </g>
          <text x="120" y="72" fill="#ffffff" font-size="46" font-weight="800" letter-spacing="-2">UpText</text>
        </svg>
        """;
    }

    private static string RenderMarkLogo(string primaryFill, string secondaryFill)
    {
        return $$"""
        <svg viewBox="0 0 160 160" role="img" aria-label="UpText mark" width="100%" height="auto">
          <rect x="24" y="24" width="46" height="46" rx="12" fill="{{primaryFill}}" />
          <rect x="90" y="24" width="46" height="30" rx="10" fill="#ffffff" />
          <rect x="24" y="90" width="38" height="38" rx="11" fill="#ffffff" />
          <rect x="82" y="82" width="54" height="54" rx="14" fill="{{secondaryFill}}" />
        </svg>
        """;
    }

    private static string RenderOutlinedLogo()
    {
        return $$"""
        <svg viewBox="0 0 160 160" role="img" aria-label="UpText outline mark" width="100%" height="auto">
          <rect x="24" y="24" width="46" height="46" rx="12" fill="none" stroke="{{InkColor}}" stroke-width="8" />
          <rect x="90" y="24" width="46" height="30" rx="10" fill="{{AccentColor}}" />
          <rect x="24" y="90" width="38" height="38" rx="11" fill="{{AccentColorSoft}}" />
          <rect x="82" y="82" width="54" height="54" rx="14" fill="none" stroke="{{InkColor}}" stroke-width="8" />
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
