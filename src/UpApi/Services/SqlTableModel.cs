namespace UpApi.Services;

internal sealed class SqlTableModel
{
    public string TableName { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public string Resource { get; set; } = string.Empty;

    public string KeyColumn { get; set; } = string.Empty;

    public bool IsIdentity { get; set; }

    public List<SqlTableColumn> Columns { get; } = [];

    public List<SqlTableColumn> NonKeyColumns { get; } = [];
}

internal sealed class SqlTableColumn
{
    public string Name { get; set; } = string.Empty;

    public string SqlType { get; set; } = string.Empty;

    public int? MaxLen { get; set; }

    public bool IsNullable { get; set; }
}
