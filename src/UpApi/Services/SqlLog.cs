using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace UpApi.Services;

public sealed class SqlLog(IOptions<SqlLogOptions> options, ILogger<SqlLog> logger) : ISqlLog
{
    private readonly SqlLogOptions sqlLogOptions = options.Value;

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetValidatedIdentifiers(out var schema, out var tableName))
        {
            return;
        }

        const string lookupTableNameParameter = "@tableName";
        const string lookupSchemaParameter = "@schema";

        var sqlCreate = $"""
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = {lookupTableNameParameter} AND TABLE_SCHEMA = {lookupSchemaParameter}
)
BEGIN
    CREATE TABLE [{schema}].[{tableName}](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [ApiName] [nvarchar](max) NULL,
        [SwaServer] [nvarchar](max) NULL,
        [MsUsed] [int] NULL,
        [TimeStamp] [datetime] DEFAULT GETDATE(),
        [ReturnValue] [int] NULL,
        [RequestBody] [nvarchar](max) NULL,
        [ReturnBody] [nvarchar](max) NULL,
        [ExecString] [nvarchar](max) NULL,
        [jwt] [nvarchar](max) NULL,
        [UnexpectedError] [nvarchar](max) NULL
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID('[{schema}].[{tableName}]')
      AND type = 'PK'
)
BEGIN
    ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    ) WITH (
        PAD_INDEX = OFF,
        STATISTICS_NORECOMPUTE = OFF,
        SORT_IN_TEMPDB = OFF,
        IGNORE_DUP_KEY = OFF,
        ONLINE = OFF,
        ALLOW_ROW_LOCKS = ON,
        ALLOW_PAGE_LOCKS = ON
    ) ON [PRIMARY]
END;
""";

        try
        {
            await using var connection = new SqlConnection(sqlLogOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sqlCreate, connection);
            command.Parameters.Add(new SqlParameter(lookupTableNameParameter, SqlDbType.NVarChar, 128) { Value = tableName });
            command.Parameters.Add(new SqlParameter(lookupSchemaParameter, SqlDbType.NVarChar, 128) { Value = schema });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize SQL log table {Schema}.{TableName}", schema, tableName);
        }
    }

    public async Task LogAsync(SqlLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!TryGetValidatedIdentifiers(out var schema, out var tableName))
        {
            return;
        }

        var sqlInsert = $"""
INSERT INTO [{schema}].[{tableName}]
    (ApiName, SwaServer, MsUsed, ReturnValue, RequestBody, ReturnBody, ExecString, jwt, UnexpectedError)
VALUES
    (@ApiName, @SwaServer, @MsUsed, @ReturnValue, @RequestBody, @ReturnBody, @ExecString, @Jwt, @UnexpectedError);
""";

        try
        {
            await using var connection = new SqlConnection(sqlLogOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sqlInsert, connection);
            command.Parameters.AddWithValue("@ApiName", entry.ApiName);
            command.Parameters.AddWithValue("@SwaServer", Environment.MachineName);
            command.Parameters.AddWithValue("@MsUsed", ParseMsUsed(entry.MsUsed));
            command.Parameters.AddWithValue("@ReturnValue", entry.ReturnValue);
            command.Parameters.AddWithValue("@RequestBody", ToDbValue(entry.RequestBody));
            command.Parameters.AddWithValue("@ReturnBody", ToDbValue(entry.ReturnBody));
            command.Parameters.AddWithValue("@ExecString", ToDbValue(entry.ExecString));
            command.Parameters.AddWithValue("@Jwt", ToDbValue(entry.Jwt));
            command.Parameters.AddWithValue("@UnexpectedError", ToDbValue(entry.UnexpectedError));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write SQL log entry for {ApiName}", entry.ApiName);
        }
    }

    private bool TryGetValidatedIdentifiers(
        [NotNullWhen(true)] out string? schema,
        [NotNullWhen(true)] out string? tableName)
    {
        schema = null;
        tableName = null;

        if (string.IsNullOrWhiteSpace(sqlLogOptions.ConnectionString))
        {
            return false;
        }

        if (!IsSafeSqlIdentifier(sqlLogOptions.Schema) || !IsSafeSqlIdentifier(sqlLogOptions.TableName))
        {
            logger.LogWarning("Skipping SQL logging because configured schema/table are not valid SQL identifiers");
            return false;
        }

        schema = sqlLogOptions.Schema;
        tableName = sqlLogOptions.TableName;
        return true;
    }

    private static bool IsSafeSqlIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static int ParseMsUsed(string value)
        => int.TryParse(value, out var msUsed) ? msUsed : 0;

    private static object ToDbValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
