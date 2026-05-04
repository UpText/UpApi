namespace UpApi.Services;

public interface ISqlWebApiExecutor
{
    Task<IResult> ExecuteAsync(
        HttpRequest request,
        string service,
        string resource,
        string? id,
        string? details,
        CancellationToken cancellationToken);
}
