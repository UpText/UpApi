using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace UpApi.Services;

internal static class JwtHelper
{
    public static string? GetClaimValue(HttpRequest request, string name, IConfiguration configuration)
    {
        var validatedToken = Parse(request, configuration);
        if (validatedToken is null)
        {
            return null;
        }

        var (principal, token) = validatedToken.Value;

        return FindClaimValue(principal.Claims, name)
            ?? FindClaimValue(token.Claims, name);
    }

    public static string? CreateToken(string jsonPayload, IReadOnlyDictionary<string, string> additionalClaims, IConfiguration configuration)
    {
        using var document = JsonDocument.Parse(jsonPayload);
        var root = document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0
            ? document.RootElement[0]
            : document.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
        {
            return null;
        }

        var claims = new List<Claim>();
        foreach (var property in root.EnumerateObject())
        {
            claims.Add(new Claim(property.Name, property.Value.ToString()));
        }

        foreach (var (key, value) in additionalClaims)
        {
            claims.Add(new Claim(key, value));
        }

        var secret = GetRequiredSetting(configuration, "JWT_SECRET");
        var issuer = GetRequiredSetting(configuration, "JWT_ISSUER");
        var audience = GetRequiredSetting(configuration, "JWT_AUDIENCE");
        var hoursRaw = GetSetting(configuration, "JWT_HOURS") ?? "8";
        if (!int.TryParse(hoursRaw, out var hours))
        {
            hours = 8;
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(hours),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static (ClaimsPrincipal Principal, JwtSecurityToken Token)? Parse(HttpRequest request, IConfiguration configuration)
    {
        var bearerToken = ReadBearerToken(request);
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return null;
        }

        var secret = GetRequiredSetting(configuration, "JWT_SECRET");
        var issuer = GetRequiredSetting(configuration, "JWT_ISSUER");
        var audience = GetRequiredSetting(configuration, "JWT_AUDIENCE");

        var principal = ValidateToken(bearerToken, issuer, audience, secret);
        if (principal is null)
        {
            return null;
        }

        return (principal, new JwtSecurityTokenHandler().ReadJwtToken(bearerToken));
    }

    private static string? FindClaimValue(IEnumerable<Claim> claims, string name)
    {
        var expectedNames = new[]
        {
            name,
            $"https://uptext.com/{name}"
        };

        foreach (var expectedName in expectedNames)
        {
            var claim = claims.FirstOrDefault(c =>
                string.Equals(c.Type, expectedName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(claim?.Value))
            {
                return claim.Value;
            }
        }

        foreach (var claim in claims)
        {
            var claimType = claim.Type;
            var slashIndex = claimType.LastIndexOf('/');
            if (slashIndex >= 0)
            {
                claimType = claimType[(slashIndex + 1)..];
            }

            if (string.Equals(claimType, name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(claim.Value))
            {
                return claim.Value;
            }
        }

        return null;
    }

    private static ClaimsPrincipal? ValidateToken(string token, string issuer, string audience, string secretKey)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            if (validatedToken is JwtSecurityToken jwt &&
                jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                return principal;
            }

            return null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        var authorizationHeader = request.Headers["Authorization"].ToString();
        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorizationHeader["Bearer ".Length..].Trim();
    }

    private static string? GetSetting(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var configuredValue = configuration[key];
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                return configuredValue;
            }

            var environmentValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }
        }

        return null;
    }

    private static string GetRequiredSetting(IConfiguration configuration, string key)
    {
        return GetSetting(configuration, key)
            ?? throw new InvalidOperationException($"{key} environment variable is not set.");
    }
}
