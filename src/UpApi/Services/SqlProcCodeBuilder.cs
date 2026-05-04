using System.Text;

namespace UpApi.Services;

internal static class SqlProcCodeBuilder
{
    public static bool IsSupportedVerb(string verb)
    {
        return verb.Equals("get", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("all", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("post", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("put", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("delete", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildTableProc(string service, string schema, SqlTableModel model, string verb, bool search, bool page, bool sort)
    {
        return verb.ToLowerInvariant() switch
        {
            "all" => BuildAllTableProcs(service, schema, model, search, page, sort),
            "get" => BuildGetTableProc(service, schema, model, search, page, sort),
            "put" => BuildPutTableProc(service, schema, model),
            "post" => BuildPostTableProc(service, schema, model),
            "delete" => BuildDeleteTableProc(service, schema, model),
            _ => string.Empty
        };
    }

    private static string BuildAllTableProcs(string service, string schema, SqlTableModel model, bool search, bool paging, bool sort)
    {
        return string.Join(
            "\r\n\r\nGO\r\n\r\n",
            BuildGetTableProc(service, schema, model, search, paging, sort).TrimEnd(),
            BuildPostTableProc(service, schema, model).TrimEnd(),
            BuildPutTableProc(service, schema, model).TrimEnd(),
            BuildDeleteTableProc(service, schema, model).TrimEnd());
    }

    private static string BuildPostTableProc(string service, string schema, SqlTableModel model)
    {
        var columns = !model.IsIdentity ? model.Columns : model.NonKeyColumns;
        var builder = new StringBuilder(6000)
            .AppendFormat("CREATE OR ALTER PROCEDURE {0}.{1}_post\r\n", service, model.Resource)
            .AppendLine("(");

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var suffix = index < columns.Count - 1 ? "," : string.Empty;
            if (column.SqlType.Contains("char", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendFormat(
                    "    @{0} {1}({2}) = NULL{3}\r\n",
                    column.Name,
                    column.SqlType,
                    column.MaxLen == -1 ? "max" : column.MaxLen?.ToString() ?? "max",
                    suffix);
            }
            else
            {
                builder.AppendFormat(
                    "    @{0} {1} = NULL{2}\r\n",
                    column.Name,
                    column.SqlType,
                    suffix);
            }
        }

        builder.AppendLine(")")
            .AppendLine("AS")
            .AppendFormat("INSERT INTO {0}.{1}\r\n", schema, model.TableName)
            .AppendLine("(");

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var suffix = index < columns.Count - 1 ? "," : string.Empty;
            builder.AppendFormat("    {0}{1}\r\n", column.Name, suffix);
        }

        builder.AppendLine(")")
            .AppendLine("VALUES")
            .AppendLine("(");

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            var suffix = index < columns.Count - 1 ? "," : string.Empty;
            builder.AppendFormat("    @{0}{1}\r\n", column.Name, suffix);
        }

        builder.AppendLine(")");
        builder.AppendFormat("DECLARE @NEWID AS VARCHAR(max) = {0}\r\n", model.IsIdentity ? "SCOPE_IDENTITY()" : "@" + model.KeyColumn);
        builder.AppendFormat("EXEC {0}.{1}_Get @ID = @NEWID\r\n", service, model.TableName);
        builder.AppendLine("RETURN 200 -- OK");
        return builder.ToString();
    }

    private static string BuildDeleteTableProc(string service, string schema, SqlTableModel model)
    {
        var builder = new StringBuilder(6000)
            .AppendFormat("CREATE OR ALTER PROCEDURE {0}.{1}_delete\r\n", service, model.Resource)
            .AppendLine("(")
            .AppendLine("    @ID varchar(max)")
            .AppendLine(")");

        builder.AppendLine("AS");
        builder.AppendFormat("IF NOT EXISTS (SELECT {2} FROM {0}.{1} WHERE @ID = {2})\r\n", schema, model.TableName, model.KeyColumn);
        builder.AppendLine("BEGIN");
        builder.AppendFormat("    RAISERROR('Unknown {0}', 1, 1)\r\n", model.Resource);
        builder.AppendLine("    RETURN 404");
        builder.AppendLine("END");
        builder.AppendFormat("DELETE FROM {0}.{1}\r\n", schema, model.TableName);
        builder.AppendFormat("WHERE @ID = {0}\r\n", model.KeyColumn);
        builder.AppendLine("RETURN 200 -- OK");
        return builder.ToString();
    }

    private static string BuildPutTableProc(string service, string schema, SqlTableModel model)
    {
        var builder = new StringBuilder(6000)
            .AppendFormat("CREATE OR ALTER PROCEDURE {0}.{1}_put\r\n", service, model.Resource)
            .AppendLine("(")
            .AppendLine("    @ID varchar(max),");

        for (var index = 0; index < model.NonKeyColumns.Count; index++)
        {
            var column = model.NonKeyColumns[index];
            var suffix = index < model.NonKeyColumns.Count - 1 ? "," : string.Empty;

            if (column.SqlType.Contains("char", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendFormat(
                    "    @{0} {1}({2}) = NULL{3}\r\n",
                    column.Name,
                    column.SqlType,
                    column.MaxLen == -1 ? "max" : column.MaxLen?.ToString() ?? "max",
                    suffix);
            }
            else
            {
                builder.AppendFormat(
                    "    @{0} {1} = NULL{2}\r\n",
                    column.Name,
                    column.SqlType,
                    suffix);
            }
        }

        builder.AppendLine(")")
            .AppendLine("AS")
            .AppendFormat("IF NOT EXISTS (SELECT {0} FROM {1}.{2} WHERE @ID = {0})\r\n", model.KeyColumn, schema, model.TableName);
        builder.AppendLine("BEGIN");
        builder.AppendFormat("    RAISERROR('Unknown {0}', 1, 1)\r\n", model.Resource);
        builder.AppendLine("    RETURN 404");
        builder.AppendLine("END");
        builder.AppendFormat("UPDATE {0}.{1}\r\n", schema, model.TableName);
        builder.AppendLine("SET");

        for (var index = 0; index < model.NonKeyColumns.Count; index++)
        {
            var column = model.NonKeyColumns[index];
            var suffix = index < model.NonKeyColumns.Count - 1 ? "," : string.Empty;
            builder.AppendFormat("    {0} = COALESCE(@{0}, {0}){1}\r\n", column.Name, suffix);
        }

        builder.AppendFormat("WHERE @ID = {0}\r\n", model.KeyColumn);
        builder.AppendFormat("EXEC {0}.{1}_Get @ID = @ID\r\n", service, model.TableName);
        builder.AppendLine("RETURN 200 -- OK");
        return builder.ToString();
    }

    private static string BuildGetTableProc(string service, string schema, SqlTableModel model, bool search, bool paging, bool sort)
    {
        var builder = new StringBuilder(6000)
            .AppendFormat("--- Retrieve {0}\r\n", model.Resource)
            .AppendFormat("CREATE OR ALTER PROCEDURE {0}.{1}_get\r\n", service, model.Resource)
            .AppendLine("(")
            .AppendLine("    @ID varchar(max) = NULL,");

        if (search)
        {
            builder.AppendLine("    @filter varchar(max) = NULL,");
        }

        if (paging)
        {
            builder.AppendLine("    @first_row int = 0,");
            builder.AppendLine("    @last_row int = 1000,");
        }

        if (sort)
        {
            builder.AppendLine("    @sort_field nvarchar(100) = NULL,");
            builder.AppendLine("    @sort_order nvarchar(4) = NULL,");
        }

        TrimTrailingCommaLine(builder);
        builder.AppendLine(")")
            .AppendLine("AS")
            .Append("SELECT ");
        builder.AppendFormat("{0} AS id", model.KeyColumn);

        for (var index = 0; index < model.Columns.Count; index++)
        {
            var column = model.Columns[index];
            if (!string.Equals(column.Name, "id", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendFormat(", {0}", column.Name);
            }
        }

        if (paging)
        {
            builder.Append(", COUNT(*) OVER() AS total_rows ");
        }

        builder.AppendFormat("\r\nFROM {0}.{1}\r\n", schema, model.TableName);
        builder.AppendFormat("WHERE (@ID IS NULL OR @ID = {0})\r\n", model.KeyColumn);

        if (search && model.Columns.Count > 1)
        {
            builder.AppendFormat("  AND (@filter IS NULL OR @filter = {0} OR CHARINDEX(@filter, CAST({1} AS varchar)) > 0)\r\n",
                model.KeyColumn, model.Columns[1].Name);
        }

        builder.AppendLine("ORDER BY");
        if (sort)
        {
            foreach (var column in model.Columns)
            {
                if (column.SqlType.Contains("char", StringComparison.OrdinalIgnoreCase) ||
                    column.SqlType.Contains("int", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendFormat("    CASE WHEN @sort_field = '{0}' AND @sort_order = 'ASC' THEN {0} END ASC,\r\n", column.Name);
                    builder.AppendFormat("    CASE WHEN @sort_field = '{0}' AND @sort_order = 'DESC' THEN {0} END DESC,\r\n", column.Name);
                }
            }

            builder.AppendFormat("    CASE WHEN @sort_field IS NULL THEN {0} END ASC\r\n", model.Columns.First().Name);
        }
        else
        {
            builder.AppendFormat("    {0}\r\n", model.Columns.First().Name);
        }

        if (paging)
        {
            builder.AppendLine("OFFSET @first_row ROWS");
            builder.AppendLine("FETCH NEXT (@last_row - @first_row + 1) ROWS ONLY");
        }

        return builder.ToString();
    }

    private static void TrimTrailingCommaLine(StringBuilder builder)
    {
        if (builder.Length < 3)
        {
            return;
        }

        var newlineLength = builder[^2] == '\r' && builder[^1] == '\n' ? 2 : 1;
        var newlineStart = builder.Length - newlineLength;
        var commaIndex = newlineStart - 1;
        if (commaIndex >= 0 && builder[commaIndex] == ',')
        {
            builder.Remove(commaIndex, 1);
        }
    }
}
