using System.Text.Json.Serialization;

namespace UpApi.Services;

internal sealed class SqlWebApiErrorResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
