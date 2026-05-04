# ServiceOpenApi

`ServiceOpenApi` builds a service-specific OpenAPI document from SQL stored procedures.

The endpoint is exposed at:

```text
/swa/{service}/swagger.json
```

For each resource/controller, the generator looks for a companion procedure:

```text
<schema>.<resource>_openapi
```

That procedure must return rows with this shape:

```sql
operation   -- target verb: get, post, put, delete, or *
class       -- target object: operation, parameter, response
name        -- item key within that object
property    -- property to set
value       -- string value to apply
```

## Naming and discovery

Resources are discovered from stored procedure names ending in:

- `get`
- `post`
- `put`
- `delete`
- `openapi`
- `ui`

Examples:

- `api.Customers_get`
- `api.Customers_post`
- `api.Customers_put`
- `api.Customers_delete`
- `api.Customers_openapi`

Controller names are normalized from the procedure name:

- the leading `_` is removed if present
- the suffix (`get`, `post`, `put`, `delete`, `openapi`, `ui`) is removed
- a trailing `_` is removed
- names are normalized to lowercase internally

That means `Customers_get` and `_customers_get` both map to the `/customers` path.

## Supported verbs

`operation` is matched against the procedure suffix and should be one of:

- `get`
- `post`
- `put`
- `delete`
- `*`

Notes:

- `*` applies to all supported classes.
- matching is case-insensitive

## Supported classes and properties

Only the combinations below are currently applied.

| class | property | name meaning | Applies to | Effect |
| --- | --- | --- | --- | --- |
| `operation` | `summary` | ignored | matching verb or `*` | Sets the OpenAPI operation summary |
| `operation` | `description` | ignored | matching verb or `*` | Sets the OpenAPI operation description |
| `operation` | `tag` | optional fallback tag value | matching verb or `*` | Adds a real OpenAPI tag |
| `parameter` | `description` | parameter name | matching verb or `*` | Sets parameter description |
| `response` | `description` | HTTP status code | matching verb or `*` | Adds or overrides a response description |

Anything else is ignored.

Examples of ignored rows:

- `class='operation', property='deprecated'`
- `class='parameter', property='required'`
- `class='response', property='contentType'`
- `class='schema', ...`

## Runtime behavior

The OpenAPI document is built from procedure metadata plus `_openapi` rows.

### Default responses

Every operation starts with:

```json
"200": { "description": "OK" }
```

You can add more response descriptions with `_openapi` rows, for example `404` or `500`.

### Parameters

Procedure parameters become OpenAPI inputs except parameters starting with `@auth_`.

- For `get` and `delete`, parameters are emitted as query parameters.
- For `post` and `put`, parameters are emitted as a JSON request body.
- Parameter names are normalized by removing the leading `@`.

### Auth parameters

Parameters named like `@auth_token` or any `@auth_*`:

- are not shown as request parameters
- mark the operation as protected
- add Bearer security metadata
- prefix the summary with a lock symbol

### SQL type to OpenAPI type mapping

Stored procedure parameter SQL types are mapped as follows:

| SQL type | OpenAPI type | format |
| --- | --- | --- |
| `bigint` | `integer` | `int64` |
| `int` | `integer` | `int32` |
| `smallint` | `integer` | `int32` |
| `tinyint` | `integer` | `int32` |
| `bit` | `boolean` | |
| `decimal` | `number` | |
| `numeric` | `number` | |
| `money` | `number` | |
| `smallmoney` | `number` | |
| `float` | `number` | `double` |
| `real` | `number` | `float` |
| `date` | `string` | `date` |
| `datetime` | `string` | `date-time` |
| `datetime2` | `string` | `date-time` |
| `smalldatetime` | `string` | `date-time` |
| `datetimeoffset` | `string` | `date-time` |
| `uniqueidentifier` | `string` | `uuid` |
| `binary` | `string` | `byte` |
| `varbinary` | `string` | `byte` |
| `image` | `string` | `byte` |
| `timestamp` | `string` | `byte` |
| `rowversion` | `string` | `byte` |
| anything else | `string` | |

Special case:

- a parameter named `password` gets format `password`

## Important implementation notes

These are current implementation details, not idealized behavior.

### `operation.tag` maps to OpenAPI tags

Rows like:

```sql
('get', 'operation', 'basic', 'tag', 'Basic tags')
```

populate the OpenAPI `tags` collection.

The tag name comes from `value`. If `value` is empty, `name` is used as a fallback.

### `operation='*'` applies to operation metadata too

This works:

```sql
('*', 'parameter', 'Id', 'description', 'Unique ID')
('*', 'response', '404', 'description', 'Not found')
('*', 'operation', '', 'summary', 'Shared summary')
('*', 'operation', '', 'tag', 'Shared tag')
```

### Output parameters are not filtered

The model records whether a parameter is output-only, but `ServiceOpenApi` currently does not exclude output parameters from the generated document.

### Unknown rows are silently ignored

There is no validation for unsupported `class` or `property` values. Unsupported rows simply have no effect.

## Recommended contract for `<resource>_openapi`

Use this result shape:

```sql
SELECT
    operation,
    class,
    name,
    property,
    value
FROM (VALUES
    ('*',      'parameter', 'Id',       'description', 'Unique ID of customer'),
    ('*',      'response',  '404',      'description', 'Customer not found in database'),
    ('get',    'operation', '',         'summary',     'Retrieve one or all customers'),
    ('get',    'operation', '',         'description', 'Get customers. Use Id to get one customer or leave it empty to get all customers'),
    ('post',   'operation', '',         'summary',     'Add a customer'),
    ('post',   'operation', '',         'description', 'Add a customer'),
    ('put',    'operation', '',         'summary',     'Update a customer'),
    ('put',    'operation', '',         'description', 'Update a customer'),
    ('delete', 'operation', '',         'summary',     'Delete a customer'),
    ('delete', 'operation', '',         'description', 'Delete a customer')
) AS Data(operation, class, name, property, value);
```

## Full example

```sql
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [api].[Customers_OpenApi]
AS
SELECT
    operation,
    class,
    name,
    property,
    value
FROM
    (VALUES
        ('*',      'parameter', 'Id',  'description', 'Unique ID of customer'),
        ('*',      'response',  '404', 'description', 'Customer not found in database'),

        ('get',    'operation', '',    'summary',     'Retrieve one or all customers'),
        ('get',    'operation', '',    'description', 'Get customers. Use Id to get one customer or leave it empty to get all customers'),

        ('post',   'operation', '',    'summary',     'Add a customer'),
        ('post',   'operation', '',    'description', 'Add a customer'),

        ('put',    'operation', '',    'summary',     'Update a customer'),
        ('put',    'operation', '',    'description', 'Update a customer'),

        ('delete', 'operation', '',    'summary',     'Delete a customer'),
        ('delete', 'operation', '',    'description', 'Delete a customer'),

        ('get',    'operation', 'basic', 'tag',       'Basic tags')
    ) AS Data(operation, class, name, property, value);
GO
```

## Equivalent Minimal API intent

The rows above are closest in intent to:

```csharp
app.MapGet("/customers", GetCustomers)
    .WithSummary("Retrieve one or all customers")
    .WithDescription("Get customers. Use Id to get one customer or leave it empty to get all customers");

app.MapPost("/customers", CreateCustomer)
    .WithSummary("Add a customer")
    .WithDescription("Add a customer");

app.MapPut("/customers", UpdateCustomer)
    .WithSummary("Update a customer")
    .WithDescription("Update a customer");

app.MapDelete("/customers", DeleteCustomer)
    .WithSummary("Delete a customer")
    .WithDescription("Delete a customer");
```

Parameter and response descriptions are also added, and `tag` now maps to real OpenAPI tags similar to `.WithTags(...)`.

## Source of truth

Current behavior is implemented in:

- [ServiceOpenApi.cs](/Users/ole/UpText/Repos/UpApi/UpApi/UpApi/Endpoints/ServiceOpenApi.cs)
- [SqlOpenApiModelBuilder.cs](/Users/ole/UpText/Repos/UpApi/UpApi/UpApi/Services/SqlOpenApiModelBuilder.cs)
