namespace UpApi.Services;

internal sealed class SqlWebApiHttpResult(
    int statusCode,
    string? contentType = null,
    string? text = null,
    byte[]? bytes = null,
    IReadOnlyDictionary<string, string>? headers = null) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            httpContext.Response.ContentType = contentType;
        }

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                httpContext.Response.Headers[key] = value;
            }
        }

        if (bytes is not null)
        {
            httpContext.Response.ContentLength = bytes.Length;
            await httpContext.Response.Body.WriteAsync(bytes.AsMemory(), httpContext.RequestAborted);
            return;
        }

        if (text is not null)
        {
            await httpContext.Response.WriteAsync(text, httpContext.RequestAborted);
        }
    }
}
