using System.Data;
using Microsoft.Data.SqlClient;

namespace UpApi.Services;

internal static class SqlTableModelBuilder
{
    public static SqlTableModel ConstructTableModel(
        string connectionString,
        string schema,
        string tableName,
        string resource,
        CancellationToken cancellationToken)
    {
        var tableModel = new SqlTableModel
        {
            TableName = tableName,
            SchemaName = schema,
            Resource = resource
        };

        using var connection = new SqlConnection(connectionString);
        using var primaryKeyCommand = connection.CreateCommand();
        using var columnsCommand = connection.CreateCommand();
        using var identityCommand = connection.CreateCommand();

        primaryKeyCommand.CommandType = CommandType.Text;
        primaryKeyCommand.CommandText = @"
SELECT b.COLUMN_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS a
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE b
    ON a.TABLE_NAME = b.TABLE_NAME
    AND a.TABLE_SCHEMA = b.TABLE_SCHEMA
    AND a.CONSTRAINT_NAME = b.CONSTRAINT_NAME
WHERE a.TABLE_NAME = @tableName
    AND a.TABLE_SCHEMA = @schema
    AND a.CONSTRAINT_TYPE = 'PRIMARY KEY'";
        primaryKeyCommand.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = tableName });
        primaryKeyCommand.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });

        columnsCommand.CommandType = CommandType.Text;
        columnsCommand.CommandText = @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @tableName
    AND TABLE_SCHEMA = @schema
ORDER BY ORDINAL_POSITION";
        columnsCommand.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = tableName });
        columnsCommand.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = schema });

        connection.Open();
        cancellationToken.ThrowIfCancellationRequested();

        var primaryKeyTable = new DataTable();
        using (var adapter = new SqlDataAdapter(primaryKeyCommand))
        {
            adapter.Fill(primaryKeyTable);
        }

        if (primaryKeyTable.Rows.Count == 0)
        {
            throw new InvalidOperationException($"Unknown table: {tableName} in schema: {schema}");
        }

        if (primaryKeyTable.Rows.Count != 1)
        {
            throw new InvalidOperationException("Table must have one primary key column.");
        }

        tableModel.KeyColumn = GetString(primaryKeyTable.Rows[0], "COLUMN_NAME");

        var columnsTable = new DataTable();
        using (var adapter = new SqlDataAdapter(columnsCommand))
        {
            adapter.Fill(columnsTable);
        }

        foreach (DataRow row in columnsTable.Rows)
        {
            tableModel.Columns.Add(new SqlTableColumn
            {
                Name = GetString(row, "COLUMN_NAME"),
                SqlType = GetString(row, "DATA_TYPE"),
                MaxLen = GetNullableInt32(row, "CHARACTER_MAXIMUM_LENGTH"),
                IsNullable = string.Equals(GetString(row, "IS_NULLABLE"), "YES", StringComparison.OrdinalIgnoreCase)
            });
        }

        identityCommand.CommandType = CommandType.Text;
        identityCommand.CommandText =
            $"SELECT columnproperty(object_id('{EscapeSqlLiteral(schema)}.{EscapeSqlLiteral(tableName)}'), '{EscapeSqlLiteral(tableModel.KeyColumn)}', 'IsIdentity')";
        var isIdentity = identityCommand.ExecuteScalar();
        tableModel.IsIdentity = isIdentity is int intValue && intValue == 1;

        foreach (var column in tableModel.Columns)
        {
            if (!string.Equals(column.Name, tableModel.KeyColumn, StringComparison.OrdinalIgnoreCase))
            {
                tableModel.NonKeyColumns.Add(column);
            }
        }

        return tableModel;
    }

    private static string GetString(DataRow row, string columnName)
    {
        return row[columnName] == DBNull.Value ? string.Empty : Convert.ToString(row[columnName]) ?? string.Empty;
    }

    private static int? GetNullableInt32(DataRow row, string columnName)
    {
        return row[columnName] == DBNull.Value ? null : Convert.ToInt32(row[columnName]);
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
