namespace UpApi.Services;

public interface ISqlLog
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);
    Task LogAsync(SqlLogEntry entry, CancellationToken cancellationToken = default);
}
