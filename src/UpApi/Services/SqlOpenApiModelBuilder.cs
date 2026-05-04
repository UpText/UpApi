using System.Data;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.OpenApi;

namespace UpApi.Services;

internal static class SqlOpenApiModelBuilder
{
    public static SqlOpenApiModel ConstructModel(string connectionString, string schema)
    {
        var model = new SqlOpenApiModel();

        using var connection = new SqlConnection(connectionString);
        using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = $@"SELECT
                    ProcedureName = ir.ROUTINE_NAME,
                    ParameterName = COALESCE(ip.PARAMETER_NAME, '<no params>'),
                    SqlType = ip.DATA_TYPE, Precision = ip.NUMERIC_PRECISION, Scale = ip.NUMERIC_SCALE, MaxLen = ip.CHARACTER_MAXIMUM_LENGTH,
                    ParameterMode = ip.PARAMETER_MODE
                FROM INFORMATION_SCHEMA.ROUTINES ir
                LEFT OUTER JOIN INFORMATION_SCHEMA.PARAMETERS ip
                    ON ir.ROUTINE_NAME = ip.SPECIFIC_NAME
                    AND ir.ROUTINE_SCHEMA = ip.SPECIFIC_SCHEMA
                WHERE ir.ROUTINE_SCHEMA = '{schema}'
                    AND ir.ROUTINE_TYPE = 'PROCEDURE'
                    AND COALESCE(OBJECTPROPERTY(OBJECT_ID(ip.SPECIFIC_NAME), 'IsMsShipped'), 0) = 0
                ORDER BY ir.ROUTINE_NAME, ip.ORDINAL_POSITION";

        var table = new DataTable();
        connection.Open();
        using (var adapter = new SqlDataAdapter(command))
        {
            adapter.Fill(table);
        }

        foreach (DataRow row in table.Rows)
        {
            var procName = GetString(row, "ProcedureName");
            var controller = GetControllerInfo(model, procName);
            if (controller is null)
            {
                continue;
            }

            var proc = GetProcInfo(controller, procName);
            var parameterName = GetString(row, "ParameterName");
            if (parameterName != "<no params>")
            {
                proc.Parameters.Add(new SqlOpenApiParameterInfo
                {
                    Name = parameterName,
                    Precision = GetInt32(row, "Precision"),
                    Scale = GetInt32(row, "Scale"),
                    MaxLen = GetInt32(row, "MaxLen"),
                    SqlType = GetString(row, "SqlType"),
                    IsOutput = GetString(row, "ParameterMode") != "IN"
                });
            }
        }

        foreach (var controller in model.Controllers)
        {
            foreach (var proc in controller.Procs)
            {
                if (proc.Name == controller.Name + "get" || proc.Name == controller.Name + "_get")
                {
                    if (controller.Columns.Count == 0)
                    {
                        ConstructEntity(connection, controller, proc, schema);
                    }
                }
            }
        }

        foreach (var controller in model.Controllers)
        {
            foreach (var proc in controller.Procs)
            {
                if (proc.Name == controller.Name + "_openapi")
                {
                    ConstructOpenApiUpdates(connection, controller, proc, schema);
                }

                if (proc.Name == controller.Name + "_ui")
                {
                    ConstructUiUpdates(connection, controller, proc, schema);
                }
            }
        }

        return model;
    }

    private static void ConstructEntity(SqlConnection connection, SqlOpenApiControllerInfo controller, SqlOpenApiProcInfo proc, string schema)
    {
        var args = string.Join(", ", proc.Parameters.Select(parameter => parameter.Name + "=null"));

        using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = $@"
SET FMTONLY ON;
EXEC {schema}.{proc.Name} {args}
SET FMTONLY OFF;";

        var table = new DataTable();
        try
        {
            using var adapter = new SqlDataAdapter(command);
            adapter.Fill(table);
        }
        catch (SqlException)
        {
            // Some stored procedures use temp tables or other constructs that fail under FMTONLY.
            // Site definition generation should remain best-effort even when column discovery fails.
            return;
        }

        foreach (DataColumn column in table.Columns)
        {
            controller.Columns.Add(new SqlOpenApiColumnInfo
            {
                Name = column.ColumnName,
                SqlType = column.DataType.Name,
                MaxLen = column.MaxLength,
                IsNullable = !string.Equals(column.ColumnName, "name", StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    private static void ConstructOpenApiUpdates(SqlConnection connection, SqlOpenApiControllerInfo controller, SqlOpenApiProcInfo proc, string schema)
    {
        using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = $"EXEC {schema}.{proc.Name}";

        var table = new DataTable();
        using var adapter = new SqlDataAdapter(command);
        adapter.Fill(table);

        foreach (DataRow row in table.Rows)
        {
            controller.OpenApiUpdates.Add(new SqlOpenApiUpdate
            {
                Name = GetString(row, "name"),
                Operation = GetString(row, "operation"),
                ClassName = GetString(row, "class"),
                Property = GetString(row, "property"),
                Value = GetString(row, "value")
            });
        }
    }

    private static void ConstructUiUpdates(SqlConnection connection, SqlOpenApiControllerInfo controller, SqlOpenApiProcInfo proc, string schema)
    {
        using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = $"EXEC {schema}.{proc.Name}";

        var table = new DataTable();
        using var adapter = new SqlDataAdapter(command);
        adapter.Fill(table);

        if (table.Rows.Count == 1 &&
            table.Columns.Count == 1 &&
            string.Equals(table.Columns[0].ColumnName, "json", StringComparison.OrdinalIgnoreCase))
        {
            controller.UiJson = GetString(table.Rows[0], "json");
        }
    }

    private static SqlOpenApiProcInfo GetProcInfo(SqlOpenApiControllerInfo controller, string procName)
    {
        procName = SkipApi(procName);
        var existing = controller.Procs.FirstOrDefault(proc => proc.Name == procName);
        if (existing is not null)
        {
            return existing;
        }

        var proc = new SqlOpenApiProcInfo { Name = procName };
        controller.Procs.Add(proc);
        return proc;
    }

    private static SqlOpenApiControllerInfo? GetControllerInfo(SqlOpenApiModel model, string procName)
    {
        procName = SkipApi(procName);
        if (procName.EndsWith("delete", StringComparison.OrdinalIgnoreCase))
        {
            procName = procName[..^6];
        }
        else if (procName.EndsWith("get", StringComparison.OrdinalIgnoreCase) || procName.EndsWith("put", StringComparison.OrdinalIgnoreCase))
        {
            procName = procName[..^3];
        }
        else if (procName.EndsWith("post", StringComparison.OrdinalIgnoreCase))
        {
            procName = procName[..^4];
        }
        else if (procName.EndsWith("openapi", StringComparison.OrdinalIgnoreCase))
        {
            procName = procName[..^7];
        }
        else if (procName.EndsWith("ui", StringComparison.OrdinalIgnoreCase))
        {
            procName = procName[..^2];
        }
        else
        {
            return null;
        }

        if (procName.EndsWith("_", StringComparison.Ordinal))
        {
            procName = procName[..^1];
        }

        var existing = model.Controllers.FirstOrDefault(controller => controller.Name == procName);
        if (existing is not null)
        {
            return existing;
        }

        var controllerInfo = new SqlOpenApiControllerInfo { Name = procName };
        model.Controllers.Add(controllerInfo);
        return controllerInfo;
    }

    private static string SkipApi(string name)
    {
        name = name.ToLowerInvariant();
        return name.StartsWith("_", StringComparison.Ordinal) ? name[1..] : name;
    }

    private static string GetString(DataRow row, string columnName)
    {
        return row[columnName] == DBNull.Value ? string.Empty : Convert.ToString(row[columnName]) ?? string.Empty;
    }

    private static int GetInt32(DataRow row, string columnName)
    {
        return row[columnName] == DBNull.Value ? 0 : Convert.ToInt32(row[columnName]);
    }
}

internal sealed class SqlOpenApiModel
{
    public List<SqlOpenApiControllerInfo> Controllers { get; } = [];
}

internal sealed class SqlOpenApiControllerInfo
{
    public string Name { get; set; } = string.Empty;
    public List<SqlOpenApiProcInfo> Procs { get; } = [];
    public List<SqlOpenApiUpdate> OpenApiUpdates { get; } = [];
    public List<SqlOpenApiColumnInfo> Columns { get; } = [];
    public string UiJson { get; set; } = string.Empty;
}

internal sealed class SqlOpenApiProcInfo
{
    public string Name { get; set; } = string.Empty;
    public List<SqlOpenApiParameterInfo> Parameters { get; } = [];

    public HttpMethod? GetVerb()
    {
        if (Name.EndsWith("get", StringComparison.OrdinalIgnoreCase)) return HttpMethod.Get;
        if (Name.EndsWith("put", StringComparison.OrdinalIgnoreCase)) return HttpMethod.Put;
        if (Name.EndsWith("post", StringComparison.OrdinalIgnoreCase)) return HttpMethod.Post;
        if (Name.EndsWith("delete", StringComparison.OrdinalIgnoreCase)) return HttpMethod.Delete;
        return null;
    }
}

internal sealed class SqlOpenApiParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public int Precision { get; set; }
    public int Scale { get; set; }
    public int MaxLen { get; set; }
    public bool IsOutput { get; set; }
}

internal sealed class SqlOpenApiUpdate
{
    public string Operation { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class SqlOpenApiColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public int MaxLen { get; set; }
    public bool IsNullable { get; set; }
}
