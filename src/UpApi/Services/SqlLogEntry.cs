namespace UpApi.Services;

public sealed record SqlLogEntry(
    string ApiName,
    string MsUsed,
    int ReturnValue,
    string RequestBody,
    string ReturnBody,
    string ExecString,
    string Jwt,
    string UnexpectedError);
