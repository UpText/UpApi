using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using UpApi.Configuration;

namespace UpApi.Services;

public sealed class SqlWebApiExecutor(
    IServiceConfigResolver serviceConfigResolver,
    IMemoryCache memoryCache,
    IConfiguration configuration,
    ISqlLog sqlLog,
    ILogger<SqlWebApiExecutor> logger) : ISqlWebApiExecutor
{
    private static readonly JsonSerializerOptions TableJsonSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new UpStringConverter(),
            new DbNullConverter(),
            new DateOnlyConverter()
        }
    };

    public async Task<IResult> ExecuteAsync(
        HttpRequest request,
        string service,
        string resource,
        string? id,
        string? details,
        CancellationToken cancellationToken)
    {
        var method = request.Method.ToLowerInvariant();
        var timer = Stopwatch.StartNew();

        if (!serviceConfigResolver.TryGet(service, out var serviceConfiguration))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "Unknown service");
        }

        var sqlSchema = serviceConfiguration.SqlSchema;
        var connectionString = serviceConfiguration.SqlConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "Missing database connectionString");
        }

        if (string.IsNullOrWhiteSpace(sqlSchema))
        {
            return ErrorResult(StatusCodes.Status400BadRequest, "Missing SqlSchema");
        }

        if (string.Equals(id, "null", StringComparison.OrdinalIgnoreCase))
        {
            id = null;
        }

        var hasIdPath = !string.IsNullOrWhiteSpace(id);
        var procedureName = details is null
            ? $"{resource}_{method}"
            : $"{resource}_{details}_{method}";

        string requestBody = string.Empty;
        JsonNode? requestJson = null;
        if (!HttpMethods.IsGet(request.Method) && request.Body is not null)
        {
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync(cancellationToken);

            if (requestBody.Length > 1 && requestBody.StartsWith("{", StringComparison.Ordinal) && requestBody.EndsWith("}", StringComparison.Ordinal))
            {
                requestJson = JsonNode.Parse(requestBody);
            }
        }

        var requestHeaders = JsonSerializer.Serialize(
            request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));

        await using var connection = new SqlConnection(connectionString);
        connection.InfoMessage += (_, args) => logger.LogInformation("PRINT:{Message}", args.Message);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand
        {
            Connection = connection,
            CommandText = $"[{sqlSchema}].{procedureName}",
            CommandType = CommandType.StoredProcedure
        };
        var sqlExec = command.CommandText;

        try
        {
            ApplyParameters(command, request, id, requestBody, requestHeaders, requestJson);
            sqlExec = BuildSqlExecString(command);

            string? jsonData = null;
            string? responseText = null;
            byte[]? responseBytes = null;
            string responseContentType = RequestContentType(request);
            var statusCode = StatusCodes.Status200OK;
            var firstRow = 0;
            int? returnedRows = null;
            int? totalRows = 0;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (reader.HasRows)
            {
                if (reader.FieldCount == 1 && string.Equals(reader.GetName(0), "json", StringComparison.OrdinalIgnoreCase))
                {
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        jsonData = reader.GetValue(0)?.ToString();
                    }
                }
                else
                {
                    var dataTable = new DataTable();
                    dataTable.Load(reader);

                    if (dataTable.Columns.Count == 1 && dataTable.Rows.Count == 1)
                    {
                        responseText = dataTable.Rows[0][0]?.ToString() ?? string.Empty;
                        responseContentType = "application/text";
                    }
                    else if (dataTable.Columns.Count == 2 &&
                             string.Equals(dataTable.Columns[0].ColumnName, "content_type", StringComparison.OrdinalIgnoreCase) &&
                             dataTable.Rows.Count == 1)
                    {
                        responseContentType = dataTable.Rows[0][0]?.ToString() ?? "application/octet-stream";
                        responseBytes = (byte[])dataTable.Rows[0][1];
                    }
                    else
                    {
                        jsonData = SerializeDataTable(dataTable);
                        returnedRows = dataTable.Rows.Count;
                        totalRows = ResolveTotalRows(dataTable);

                        if (dataTable.Rows.Count == 1 && (hasIdPath || HttpMethods.IsPost(request.Method)))
                        {
                            var array = JsonNode.Parse(jsonData);
                            if (array?[0] is not null)
                            {
                                jsonData = array[0]!.ToJsonString();
                            }
                        }
                    }
                }
            }
            else if (!hasIdPath)
            {
                jsonData = "[]";
                returnedRows = 0;
                totalRows = 0;
            }

            await reader.CloseAsync();

            statusCode = ResolveStatusCode(command);

            if (statusCode < 299)
            {
                var outputResult = BuildOutputPayload(command, jsonData, totalRows);
                jsonData = outputResult.JsonData;
                totalRows = outputResult.TotalRows;
            }

            if (string.Equals(procedureName, "login_post", StringComparison.OrdinalIgnoreCase) &&
                responseBytes is null)
            {
                var bodyForToken = responseText ?? jsonData;
                if (string.IsNullOrWhiteSpace(bodyForToken))
                {
                    return await LogAndReturnAsync(
                        new SqlWebApiHttpResult(statusCode: StatusCodes.Status404NotFound),
                        sqlSchema,
                        sqlExec,
                        requestBody,
                        timer,
                        StatusCodes.Status404NotFound,
                        string.Empty,
                        string.Empty,
                        cancellationToken);
                }

                var baseUrl = $"{request.Scheme}://{request.Host}";
                var token = JwtHelper.CreateToken(
                    bodyForToken,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["baseurl"] = baseUrl,
                        ["service"] = service
                    },
                    configuration);

                if (token is null)
                {
                    return await LogAndReturnAsync(
                        ErrorResult(StatusCodes.Status500InternalServerError, "Token generation failed"),
                        sqlSchema,
                        sqlExec,
                        requestBody,
                        timer,
                        StatusCodes.Status500InternalServerError,
                        "Token generation failed",
                        "Token generation failed",
                        cancellationToken);
                }

                var tokenResponse = JsonSerializer.Serialize(new { token });
                return await LogAndReturnAsync(
                    JsonResult(new { token }, StatusCodes.Status200OK),
                    sqlSchema,
                    sqlExec,
                    requestBody,
                    timer,
                    StatusCodes.Status200OK,
                    tokenResponse,
                    string.Empty,
                    cancellationToken,
                    token);
            }

            if (hasIdPath && IsEmptyJsonArray(jsonData))
            {
                jsonData = string.Empty;
                statusCode = StatusCodes.Status404NotFound;
            }

            if (responseBytes is not null)
            {
                return await LogAndReturnAsync(
                    new SqlWebApiHttpResult(
                    statusCode: statusCode,
                    contentType: responseContentType,
                    bytes: responseBytes),
                    sqlSchema,
                    sqlExec,
                    requestBody,
                    timer,
                    statusCode,
                    string.Empty,
                    string.Empty,
                    cancellationToken);
            }

            if (responseText is not null)
            {
                return await LogAndReturnAsync(
                    new SqlWebApiHttpResult(
                    statusCode: statusCode,
                    contentType: responseContentType,
                    text: responseText),
                    sqlSchema,
                    sqlExec,
                    requestBody,
                    timer,
                    statusCode,
                    responseText,
                    string.Empty,
                    cancellationToken);
            }

            string? responseBody;
            if (!string.IsNullOrWhiteSpace(jsonData))
            {
                responseBody = jsonData;
                responseContentType = "application/json";
            }
            else if (!HttpMethods.IsGet(request.Method))
            {
                responseBody = string.Empty;
            }
            else if (hasIdPath)
            {
                responseBody = null;
                statusCode = StatusCodes.Status404NotFound;
            }
            else
            {
                responseBody = string.Empty;
            }

            if (!hasIdPath && returnedRows is null)
            {
                returnedRows = CountJsonArrayItems(jsonData);
            }

            if (!hasIdPath && totalRows is null)
            {
                totalRows = returnedRows ?? 0;
            }

            Dictionary<string, string>? headers = null;
            if (HttpMethods.IsGet(request.Method) && !hasIdPath && statusCode < 300 && totalRows is not null && returnedRows is not null)
            {
                firstRow = TryParseRangeStart(request.Query["range"]);
                var lastRow = returnedRows == 0 ? firstRow : firstRow + returnedRows.Value - 1;
                headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Access-Control-Expose-Headers"] = "Content-Range",
                    ["Content-Range"] = $"{firstRow}-{lastRow}/{totalRows}"
                };
            }

            return await LogAndReturnAsync(
                new SqlWebApiHttpResult(
                    statusCode: statusCode,
                    contentType: responseContentType,
                    text: responseBody,
                    headers: headers),
                sqlSchema,
                sqlExec,
                requestBody,
                timer,
                statusCode,
                responseBody ?? string.Empty,
                string.Empty,
                cancellationToken);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Sql exception while executing {Procedure}", command.CommandText);
            var statusCode = ResolveStatusCode(command);
            if (statusCode < 400)
            {
                statusCode = StatusCodes.Status500InternalServerError;
            }

            var errorText = string.Join("\n", ex.Errors.Cast<SqlError>().Select(error => error.Message));
            var publicMessage = statusCode == 500 ? "Database Error" : errorText;
            return await LogAndReturnAsync(
                ErrorResult(statusCode, publicMessage),
                sqlSchema,
                sqlExec,
                requestBody,
                timer,
                statusCode,
                publicMessage,
                statusCode >= 500 ? errorText : string.Empty,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while executing {Procedure}", command.CommandText);
            return await LogAndReturnAsync(
                ErrorResult(StatusCodes.Status400BadRequest, ex.Message),
                sqlSchema,
                command.CommandText,
                requestBody,
                timer,
                StatusCodes.Status400BadRequest,
                ex.Message,
                ex.Message,
                cancellationToken);
        }
    }

    private async Task<IResult> LogAndReturnAsync(
        IResult result,
        string apiName,
        string execString,
        string requestBody,
        Stopwatch timer,
        int statusCode,
        string returnBody,
        string unexpectedError,
        CancellationToken cancellationToken,
        string jwt = "")
    {
        if (!timer.IsRunning)
        {
            return result;
        }

        timer.Stop();
        await sqlLog.LogAsync(
            new SqlLogEntry(
                ApiName: apiName,
                MsUsed: timer.ElapsedMilliseconds.ToString(),
                ReturnValue: statusCode,
                RequestBody: requestBody,
                ReturnBody: returnBody,
                ExecString: execString,
                Jwt: jwt,
                UnexpectedError: unexpectedError),
            cancellationToken);

        return result;
    }

    private void ApplyParameters(
        SqlCommand command,
        HttpRequest request,
        string? id,
        string requestBody,
        string requestHeaders,
        JsonNode? requestJson)
    {
        SqlParameter[] cachedParameters;
        if (!memoryCache.TryGetValue(command.CommandText, out cachedParameters!))
        {
            SqlCommandBuilder.DeriveParameters(command);
            cachedParameters = CloneParameters(command.Parameters);
            memoryCache.Set(command.CommandText, cachedParameters, TimeSpan.FromSeconds(30));
        }
        else
        {
            command.Parameters.Clear();
            foreach (var parameter in cachedParameters)
            {
                command.Parameters.Add(((ICloneable)parameter).Clone());
            }
        }

        ApplyRangeAndSortParameters(command, request.Query);
        ApplyQueryParameters(command, request.Query);

        if (command.Parameters.Contains("@id") && !request.Query.ContainsKey("id"))
        {
            command.Parameters["@id"].Value = string.IsNullOrWhiteSpace(id) ? DBNull.Value : id;
        }

        if (command.Parameters.Contains("@requestBody"))
        {
            command.Parameters["@requestBody"].Value = requestBody;
        }

        if (command.Parameters.Contains("@requestHeaders"))
        {
            command.Parameters["@requestHeaders"].Value = requestHeaders;
        }

        RemoveOptionalRangeAndSortParameters(command, request.Query);

        if (requestJson is not null)
        {
            foreach (SqlParameter parameter in command.Parameters)
            {
                if (parameter.Direction is not (ParameterDirection.Input or ParameterDirection.InputOutput))
                {
                    continue;
                }

                var paramName = parameter.ParameterName[1..];
                var jsonValue = requestJson[paramName];
                if (jsonValue is null)
                {
                    continue;
                }

                parameter.Value = JsonNodeToSqlValue(jsonValue);
            }
        }

        foreach (SqlParameter parameter in command.Parameters)
        {
            if (string.Equals(parameter.ParameterName, "@url", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = request.GetDisplayUrl();
                continue;
            }

            if (parameter.ParameterName.StartsWith("@auth_", StringComparison.OrdinalIgnoreCase))
            {
                var claimName = parameter.ParameterName[6..];
                var claimValue = JwtHelper.GetClaimValue(request, claimName, configuration);
                if (string.IsNullOrWhiteSpace(claimValue))
                {
                    throw new InvalidOperationException("Missing or invalid security header");
                }

                parameter.Value = claimValue;
                continue;
            }

            if (string.Equals(parameter.ParameterName, "@passwordHash", StringComparison.OrdinalIgnoreCase))
            {
                var password = request.Query["password"].FirstOrDefault() ?? requestJson?["password"]?.ToString();
                if (!string.IsNullOrWhiteSpace(password))
                {
                    parameter.Value = UpHasher.HashPassword(password);
                }
            }

            if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput &&
                parameter.Value is null)
            {
                parameter.Value = DBNull.Value;
            }
        }

    }

    private static void RemoveOptionalRangeAndSortParameters(SqlCommand command, IQueryCollection query)
    {
        if (!query.ContainsKey("range"))
        {
            RemoveOptionalInputParameter(command, "@first_row");
            RemoveOptionalInputParameter(command, "@last_row");
        }

        if (!query.ContainsKey("sort"))
        {
            RemoveOptionalInputParameter(command, "@sort_field");
            RemoveOptionalInputParameter(command, "@sort_order");
        }
    }

    private static void RemoveOptionalInputParameter(SqlCommand command, string parameterName)
    {
        if (!command.Parameters.Contains(parameterName))
        {
            return;
        }

        var parameter = command.Parameters[parameterName];
        if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput &&
            parameter.Value is null)
        {
            command.Parameters.Remove(parameter);
        }
    }

    private static void ApplyRangeAndSortParameters(SqlCommand command, IQueryCollection query)
    {
        if (query.TryGetValue("range", out var rangeValue) &&
            TryParseJsonArray(rangeValue.ToString(), out var rangeArray) &&
            rangeArray?.Count >= 2)
        {
            if (command.Parameters.Contains("@first_row") &&
                command.Parameters["@first_row"].Direction == ParameterDirection.Input)
            {
                command.Parameters["@first_row"].Value = rangeArray[0]?.GetValue<int>() ?? 0;
            }

            if (command.Parameters.Contains("@last_row") &&
                command.Parameters["@last_row"].Direction == ParameterDirection.Input)
            {
                command.Parameters["@last_row"].Value = rangeArray[1]?.GetValue<int>() ?? 100;
            }
        }

        if (query.TryGetValue("sort", out var sortValue) &&
            TryParseJsonArray(sortValue.ToString(), out var sortArray) &&
            sortArray?.Count >= 2)
        {
            if (command.Parameters.Contains("@sort_field") &&
                command.Parameters["@sort_field"].Direction == ParameterDirection.Input)
            {
                command.Parameters["@sort_field"].Value = sortArray[0]?.ToString() ?? string.Empty;
            }

            if (command.Parameters.Contains("@sort_order") &&
                command.Parameters["@sort_order"].Direction == ParameterDirection.Input)
            {
                command.Parameters["@sort_order"].Value = sortArray[1]?.ToString() ?? string.Empty;
            }
        }
    }

    private static void ApplyQueryParameters(SqlCommand command, IQueryCollection query)
    {
        foreach (var (key, value) in query)
        {
            if (string.Equals(key, "range", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameterName = "@" + key.Trim();
            if (command.Parameters.Contains(parameterName) &&
                command.Parameters[parameterName].Direction == ParameterDirection.Input)
            {
                command.Parameters[parameterName].Value = value.ToString();
            }
        }
    }

    private static object JsonNodeToSqlValue(JsonNode value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Number => document.RootElement.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Array or JsonValueKind.Object => value.ToJsonString(),
            JsonValueKind.Null => DBNull.Value,
            _ => value.ToString()
        };
    }

    private static SqlParameter[] CloneParameters(DbParameterCollection parameters)
    {
        var clone = new SqlParameter[parameters.Count];
        for (var index = 0; index < parameters.Count; index++)
        {
            clone[index] = (SqlParameter)((ICloneable)parameters[index]).Clone();
        }

        return clone;
    }

    private static string SerializeDataTable(DataTable table)
    {
        var tableData = table.Rows.OfType<DataRow>()
            .Select(row => table.Columns.OfType<DataColumn>()
                .ToDictionary(column => column.ColumnName, column => row[column]));

        return JsonSerializer.Serialize(tableData, TableJsonSerializerOptions);
    }

    private static int? ResolveTotalRows(DataTable table)
    {
        if (table.Rows.Count == 0)
        {
            return 0;
        }

        if (table.Columns.Contains("total_rows"))
        {
            return int.Parse(table.Rows[0]["total_rows"].ToString()!, System.Globalization.CultureInfo.InvariantCulture);
        }

        return table.Rows.Count;
    }

    private static int ResolveStatusCode(SqlCommand command)
    {
        if (command.Parameters.Contains("@RETURN_VALUE") &&
            command.Parameters["@RETURN_VALUE"].Value is not null &&
            int.TryParse(command.Parameters["@RETURN_VALUE"].Value.ToString(), out var returnCode) &&
            returnCode > 0)
        {
            return returnCode;
        }

        return StatusCodes.Status200OK;
    }

    private static string BuildSqlExecString(SqlCommand command)
    {
        var sqlExec = new StringBuilder("EXEC ");
        sqlExec.Append(command.CommandText);
        sqlExec.Append(' ');

        foreach (SqlParameter parameter in command.Parameters)
        {
            if (parameter.Direction is not (ParameterDirection.Input or ParameterDirection.InputOutput))
            {
                continue;
            }

            if (parameter.Value is null or DBNull)
            {
                continue;
            }

            sqlExec.Append(parameter.ParameterName);
            sqlExec.Append("='");
            sqlExec.Append(parameter.Value.ToString()?.Replace("'", "''", StringComparison.Ordinal) ?? string.Empty);
            sqlExec.Append("',");
        }

        if (sqlExec[^1] == ',')
        {
            sqlExec.Length--;
        }

        return sqlExec.ToString();
    }

    private static (string? JsonData, int? TotalRows) BuildOutputPayload(SqlCommand command, string? jsonData, int? totalRows)
    {
        var hasBodyOutput = false;
        var isCompound = false;

        foreach (SqlParameter parameter in command.Parameters)
        {
            if (parameter.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
            {
                continue;
            }

            if (string.Equals(parameter.ParameterName, "@body", StringComparison.OrdinalIgnoreCase))
            {
                hasBodyOutput = true;
            }
            else if (string.Equals(parameter.ParameterName, "@total_rows", StringComparison.OrdinalIgnoreCase))
            {
                totalRows = parameter.Value is null || parameter.Value == DBNull.Value
                    ? 0
                    : int.Parse(parameter.Value.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (!string.Equals(parameter.ParameterName, "@ui", StringComparison.OrdinalIgnoreCase))
            {
                isCompound = true;
            }
        }

        if (hasBodyOutput)
        {
            return (Convert.ToString(command.Parameters["@body"].Value), totalRows);
        }

        if (!isCompound)
        {
            return (jsonData, totalRows);
        }

        var responseObject = new JsonObject();
        if (!string.IsNullOrWhiteSpace(jsonData))
        {
            responseObject.Add("data", JsonNode.Parse(jsonData));
        }

        foreach (SqlParameter parameter in command.Parameters)
        {
            if (parameter.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
            {
                continue;
            }

            var paramName = parameter.ParameterName[1..];
            var paramValue = Convert.ToString(parameter.Value) ?? string.Empty;
            try
            {
                responseObject.Add(paramName, JsonNode.Parse(paramValue));
            }
            catch (JsonException)
            {
                responseObject.Add(paramName, paramValue);
            }
        }

        return (responseObject.ToJsonString(), totalRows);
    }

    private static bool IsEmptyJsonArray(string? jsonData)
    {
        if (string.IsNullOrWhiteSpace(jsonData))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonData);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? CountJsonArrayItems(string? jsonData)
    {
        if (string.IsNullOrWhiteSpace(jsonData))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonData);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int TryParseRangeStart(string? range)
    {
        if (!TryParseJsonArray(range, out var array) || array is null || array.Count == 0)
        {
            return 0;
        }

        return array[0] is JsonValue value
            ? value.GetValue<int>()
            : 0;
    }

    private static bool TryParseJsonArray(string? json, out JsonArray? array)
    {
        array = null;
        if (string.IsNullOrWhiteSpace(json) || json[0] != '[')
        {
            return false;
        }

        try
        {
            array = JsonNode.Parse(json)?.AsArray();
            return array is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string RequestContentType(HttpRequest request)
    {
        return string.IsNullOrWhiteSpace(request.ContentType) ? "application/json" : request.ContentType;
    }

    private static IResult ErrorResult(int statusCode, string message)
    {
        return new SqlWebApiHttpResult(
            statusCode: statusCode,
            contentType: "application/json",
            text: JsonSerializer.Serialize(new SqlWebApiErrorResponse { Message = statusCode == 500 ? "Database Error" : message }));
    }

    private static IResult JsonResult(object value, int statusCode)
    {
        return new SqlWebApiHttpResult(
            statusCode: statusCode,
            contentType: "application/json",
            text: JsonSerializer.Serialize(value));
    }
}
