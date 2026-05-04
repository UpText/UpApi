using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using UpApi.Configuration;

namespace UpApi.Endpoints;

public static partial class SqlImageFunc
{
    public static IEndpointRouteBuilder MapSqlImageFunc(this IEndpointRouteBuilder app)
    {
        app.MapGet("/swa/images/{service}/{resource}/{id}", HandleAsync)
            .WithName("SqlImageFunc")
            .WithTags("BuiltIn")
            .WithSummary("Return an image from SQL storage")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status404NotFound, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        string service,
        string resource,
        string id,
        HttpRequest request,
        IServiceConfigResolver serviceConfigResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("SqlImageFunc");

        try
        {
            if (!serviceConfigResolver.TryGet(service, out var serviceConfiguration))
            {
                return Results.Text("Service configuration was not found.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
            }

            var sqlSchema = serviceConfiguration.SqlSchema;
            var connectionString = serviceConfiguration.SqlConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Results.Text("SqlConnectionString is not configured.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
            }

            if (string.IsNullOrWhiteSpace(sqlSchema))
            {
                return Results.Text("SqlSchema is not configured.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.Text("Image id is required.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
            }

            if (!int.TryParse(id, out var imageId))
            {
                return Results.Text("Image id must be an integer.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
            }

            if (!IsSafeSqlIdentifier(sqlSchema) || !IsSafeSqlIdentifier(resource))
            {
                return Results.Text("Invalid schema or resource.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
            }

            var raw = string.Equals(request.Query["raw"], "true", StringComparison.OrdinalIgnoreCase);
            var blob = await ReadCategoryImageAsync(connectionString, sqlSchema, resource, imageId, cancellationToken);

            if (blob == null || blob.Length == 0)
            {
                return Results.Text("Image not found.", "text/plain", statusCode: StatusCodes.Status404NotFound);
            }

            if (raw)
            {
                var rawBytes = TryExtractBmpFromOle(blob) ?? blob;
                return Results.File(rawBytes, "application/octet-stream", enableRangeProcessing: false, lastModified: null)
                    .WithCache();
            }

            var processed = TryExtractBmpFromOle(blob) ?? blob;

            try
            {
                await using var inStream = new MemoryStream(processed, writable: false);
                await using var outStream = new MemoryStream();
                using var image = await Image.LoadAsync(inStream, cancellationToken);
                await image.SaveAsync(outStream, new PngEncoder(), cancellationToken);

                return Results.File(outStream.ToArray(), "image/png", enableRangeProcessing: false, lastModified: null)
                    .WithCache();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to decode image; returning raw bytes.");
                return Results.File(blob, "application/octet-stream", enableRangeProcessing: false, lastModified: null)
                    .WithCache();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in GetCategoryImage");
            return Results.Text("Unhandled server error.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<byte[]?> ReadCategoryImageAsync(
        string connectionString,
        string schema,
        string resource,
        int id,
        CancellationToken cancellationToken)
    {
        var procedureName = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(resource)}_image";
        var sql = $"exec {procedureName} @id=@id";

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result == null || result is DBNull ? null : (byte[])result;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static byte[]? TryExtractBmpFromOle(byte[] data)
    {
        for (var index = 0; index < data.Length - 1; index++)
        {
            if (data[index] == 0x42 && data[index + 1] == 0x4D)
            {
                var length = data.Length - index;
                var slice = new byte[length];
                Buffer.BlockCopy(data, index, slice, 0, length);
                return slice;
            }
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SafeSqlIdentifierRegex();

    private static bool IsSafeSqlIdentifier(string value)
    {
        return SafeSqlIdentifierRegex().IsMatch(value);
    }

    private static IResult WithCache(this IResult result)
    {
        return new CachedResult(result);
    }

    private sealed class CachedResult(IResult inner) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "public, max-age=3600";
            await inner.ExecuteAsync(httpContext);
        }
    }
}
