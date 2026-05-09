# UpApi

`UpApi` turns SQL Server stored procedures into a REST API.

You define your API in SQL, not in controllers:

- Create stored procedures like `Customers_get`, `Customers_post`, `Customers_put`, and `Customers_delete`
- Point `UpApi` at a SQL Server database and schema
- Run the app from source or as a container
- Get REST endpoints, JWT protection, OpenAPI, and Swagger UI automatically

It is built for teams that want a simple SQL-first API layer with very little moving code.

## What It Gives You

- SQL-first REST API generation from stored procedures
- SQL Server support
- No C# endpoint code required for each resource
- Run from source with `dotnet run`
- Run in Docker as a container
- Automatic OpenAPI generation per service
- Built-in Swagger UI
- JWT token creation for login endpoints
- JWT-based endpoint protection using SQL parameter naming
- Paging and sorting support through stored procedure parameters
- Binary/file responses from SQL
- Multi-service support through configuration

## How It Works

`UpApi` maps HTTP verbs to stored procedure names:

- `GET /swa/{service}/customers` -> `[schema].[customers_get]`
- `GET /swa/{service}/customers/1` -> `[schema].[customers_get] @id = 1`
- `POST /swa/{service}/customers` -> `[schema].[customers_post]`
- `PUT /swa/{service}/customers/1` -> `[schema].[customers_put] @id = 1`
- `DELETE /swa/{service}/customers/1` -> `[schema].[customers_delete] @id = 1`

The `{service}` part comes from configuration. Each service points to:

- a SQL connection string
- a SQL schema that contains the API stored procedures

Example:

```json
{
  "Services": {
    "api": {
      "SqlSchema": "api",
      "SqlConnectionString": "Server=localhost,1433;Initial Catalog=MyDb;User ID=upservice;Password=VerySecret!321;Encrypt=False;TrustServerCertificate=True;"
    }
  }
}
```

## Requirements

- SQL Server
- .NET 10 SDK to run from source
- Docker, if you want to run the container image

## Project Layout

- [`src/UpApi/Program.cs`](/Users/ole/UpText/Repos/UpApi/src/UpApi/Program.cs) bootstraps the API
- [`src/UpApi/Endpoints/SqlWebApi.cs`](/Users/ole/UpText/Repos/UpApi/src/UpApi/Endpoints/SqlWebApi.cs) exposes the SQL-backed REST endpoints
- [`src/UpApi/Endpoints/ServiceOpenApi.cs`](/Users/ole/UpText/Repos/UpApi/src/UpApi/Endpoints/ServiceOpenApi.cs) generates service-specific OpenAPI
- [`src/UpApi/Endpoints/SwaggerUi.cs`](/Users/ole/UpText/Repos/UpApi/src/UpApi/Endpoints/SwaggerUi.cs) serves Swagger UI
- [`src/UpApi/Endpoints/SqlGenFunc.cs`](/Users/ole/UpText/Repos/UpApi/src/UpApi/Endpoints/SqlGenFunc.cs) can generate CRUD stored procedure templates

## Quick Start

### 1. Configure a service

Update [`src/UpApi/appsettings.json`](/Users/ole/UpText/Repos/UpApi/src/UpApi/appsettings.json) or provide environment variables.

Minimal example:

```json
{
  "JWT_SECRET": "replace-with-a-long-random-secret",
  "JWT_ISSUER": "UpApi",
  "JWT_AUDIENCE": "UpApiClient",
  "JWT_HOURS": "8",
  "Cors": {
    "AllowedOrigins": []
  },
  "Services": {
    "api": {
      "SqlSchema": "api",
      "SqlConnectionString": "Server=localhost,1433;Initial Catalog=MyDb;User ID=upservice;Password=VerySecret!321;Encrypt=False;TrustServerCertificate=True;"
    }
  }
}
```

Recommended:

- keep real secrets out of source control
- use environment variables in production
- keep one SQL schema per API service

### CORS configuration

CORS is now fully configuration-driven.

Set allowed origins in:

- [`src/UpApi/appsettings.Development.json`](/Users/ole/UpText/Repos/UpApi/src/UpApi/appsettings.Development.json) for local development
- [`src/UpApi/appsettings.json`](/Users/ole/UpText/Repos/UpApi/src/UpApi/appsettings.json) or environment variables for shared environments and containers

Example:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://app.example.com",
      "https://admin.example.com"
    ]
  }
}
```

If `Cors:AllowedOrigins` is empty, `UpApi` does not send CORS allow headers, so cross-origin browser requests are not allowed.

### SQL logging

`UpApi` can write one row per API execution to SQL Server. This is useful when you want a simple audit trail for requests, status codes, execution time, generated SQL exec strings, request bodies, response bodies, and unexpected errors.

Configure it with a `SqlLog` section:

```json
{
  "SqlLog": {
    "ConnectionString": "Server=localhost,1433;Initial Catalog=MyDb;User ID=upservice;Password=VerySecret!321;Encrypt=False;TrustServerCertificate=True;",
    "Schema": "api",
    "TableName": "log"
  }
}
```

Notes:

- `ConnectionString` is optional if `SqlServerLogDb` is set instead
- `Schema` defaults to `dbo`, but `api` is recommended so the log table stays with the rest of the service objects
- `TableName` defaults to `log`
- the schema and table name must be simple SQL identifiers using letters, numbers, or `_`

For local development, `UpApi` can create the log table automatically at startup if it does not already exist.

For production, it is better to have `dbo` pre-create `api.log` and grant only `INSERT` to `upservice`. That keeps the runtime account from needing table-creation permissions.

Recommended setup:

```sql
CREATE TABLE [api].[log](
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
    [UnexpectedError] [nvarchar](max) NULL,
    CONSTRAINT [PK_api_log] PRIMARY KEY CLUSTERED ([Id] ASC)
);

GRANT INSERT ON [api].[log] TO upservice;
```

Typical uses:

- trace API calls during development
- inspect request and response payloads when debugging procedure behavior
- monitor status codes and execution times
- capture unexpected SQL or application errors for later review

If logging is enabled, every executed endpoint writes a row to the configured log table.

### 2. Create the SQL service user

`UpApi` only needs permission to connect to the database and execute stored procedures in the configured schema. Using a dedicated service login instead of `sa` keeps local setup closer to production and limits the blast radius if the API credentials are exposed.

Run this in SQL Server before starting the API:

```sql
CREATE LOGIN upservice WITH PASSWORD = 'VerySecret!321';
CREATE SCHEMA api AUTHORIZATION dbo;
CREATE USER upservice FOR LOGIN upservice;
GRANT CONNECT TO upservice;
GRANT EXECUTE ON SCHEMA::[api] TO upservice;
GRANT INSERT ON [api].[log] TO upservice;
```

This matches the example connection string above and gives `UpApi` access to execute procedures in the `api` schema and write to `api.log` without broader database permissions.

### 3. Create stored procedures

Create your procedures in SQL Server under the configured schema, for example:

- `api.Customers_get`
- `api.Customers_post`
- `api.Customers_put`
- `api.Customers_delete`
- `api.Customers_openapi` optional, for richer docs

### 4. Run from source

```bash
dotnet run --project /Users/ole/UpText/Repos/UpApi/src/UpApi/UpApi.csproj
```

Default local development URL from launch settings:

- `http://localhost:5092`

Useful built-in routes:

- `/`
- `/ping`
- `/docs`
- `/docs/api`
- `/swagger.json`
- `/swa/api/swagger.json`

### 5. Run as a container

Build manually:

```bash
docker build -f /Users/ole/UpText/Repos/UpApi/src/UpApi/Dockerfile -t upapi /Users/ole/UpText/Repos/UpApi/src
```

Run:

```bash
docker run --rm \
  -p 8080:8080 \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e JWT_SECRET=replace-with-a-long-random-secret \
  -e JWT_ISSUER=UpApi \
  -e JWT_AUDIENCE=UpApiClient \
  -e JWT_HOURS=8 \
  -e Cors__AllowedOrigins__0=https://app.example.com \
  -e Cors__AllowedOrigins__1=https://admin.example.com \
  -e Services__api__SqlSchema=api \
  -e Services__api__SqlConnectionString="Server=host.docker.internal,1433;Initial Catalog=MyDb;User ID=upservice;Password=VerySecret!321;Encrypt=False;TrustServerCertificate=True;" \
  upapi
```

## REST Route Convention

The main route is:

```text
/swa/{service}/{resource}
/swa/{service}/{resource}/{id}
```

Examples:

```text
GET    /swa/api/customers
GET    /swa/api/customers/1
POST   /swa/api/customers
PUT    /swa/api/customers/1
DELETE /swa/api/customers/1
```

Stored procedure naming:

```text
customers_get
customers_post
customers_put
customers_delete
```

The configured SQL schema is prepended automatically, for example `api.customers_get`.

## Simple CRUD Example

### Table

```sql
CREATE TABLE dbo.Customers
(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name nvarchar(100) NOT NULL,
    Email nvarchar(200) NULL
);
GO
```

### GET

```sql
CREATE OR ALTER PROCEDURE api.Customers_get
    @Id varchar(max) = NULL
AS
SELECT
    Id,
    Name,
    Email
FROM dbo.Customers
WHERE @Id IS NULL OR Id = TRY_CAST(@Id AS int)
ORDER BY Id;
GO
```

### POST

```sql
CREATE OR ALTER PROCEDURE api.Customers_post
    @Name nvarchar(100),
    @Email nvarchar(200) = NULL
AS
INSERT INTO dbo.Customers (Name, Email)
VALUES (@Name, @Email);

DECLARE @NewId int = SCOPE_IDENTITY();

EXEC api.Customers_get @Id = @NewId;
RETURN 200;
GO
```

### PUT

```sql
CREATE OR ALTER PROCEDURE api.Customers_put
    @Id varchar(max),
    @Name nvarchar(100) = NULL,
    @Email nvarchar(200) = NULL
AS
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = TRY_CAST(@Id AS int))
BEGIN
    RAISERROR('Unknown customer', 1, 1);
    RETURN 404;
END;

UPDATE dbo.Customers
SET
    Name = COALESCE(@Name, Name),
    Email = COALESCE(@Email, Email)
WHERE Id = TRY_CAST(@Id AS int);

EXEC api.Customers_get @Id = @Id;
RETURN 200;
GO
```

### DELETE

```sql
CREATE OR ALTER PROCEDURE api.Customers_delete
    @Id varchar(max)
AS
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = TRY_CAST(@Id AS int))
BEGIN
    RAISERROR('Unknown customer', 1, 1);
    RETURN 404;
END;

DELETE FROM dbo.Customers
WHERE Id = TRY_CAST(@Id AS int);

RETURN 200;
GO
```

### Test the CRUD API

List customers:

```bash
curl http://localhost:5092/swa/api/customers
```

Get one:

```bash
curl http://localhost:5092/swa/api/customers/1
```

Create:

```bash
curl -X POST http://localhost:5092/swa/api/customers \
  -H "Content-Type: application/json" \
  -d '{"name":"Ada Lovelace","email":"ada@example.com"}'
```

Update:

```bash
curl -X PUT http://localhost:5092/swa/api/customers/1 \
  -H "Content-Type: application/json" \
  -d '{"email":"ada@uptext.example"}'
```

Delete:

```bash
curl -X DELETE http://localhost:5092/swa/api/customers/1
```

## JWT Authentication

JWT is built in.

Two important conventions are used:

### 1. `login_post` can generate a token

If you create a procedure named `login_post`, `UpApi` can turn its result into a JWT response:

```text
POST /swa/api/login
```

If the procedure returns a row or JSON object with claims, the API responds with:

```json
{
  "token": "..."
}
```

### 2. `@auth_*` parameters protect endpoints

If a stored procedure has parameters like:

```sql
@auth_userid nvarchar(100)
```

then `UpApi`:

- expects `Authorization: Bearer <token>`
- validates the JWT
- reads the matching claim from the token
- injects it into the stored procedure parameter
- marks the endpoint as secured in OpenAPI

Example protected procedure:

```sql
CREATE OR ALTER PROCEDURE api.Profile_get
    @auth_userid nvarchar(100)
AS
SELECT
    UserId,
    DisplayName,
    Email
FROM dbo.Users
WHERE UserId = @auth_userid;
GO
```

Call it:

```bash
curl http://localhost:5092/swa/api/profile \
  -H "Authorization: Bearer YOUR_TOKEN"
```

## OpenAPI And Swagger UI

`UpApi` generates documentation automatically.

Built-in docs endpoints:

- `/swagger.json` for the app-level OpenAPI document
- `/swa/{service}/swagger.json` for service-specific SQL-generated OpenAPI
- `/docs` for Swagger UI
- `/docs/{service}` for a service Swagger UI page

Examples:

```text
http://localhost:5092/docs
http://localhost:5092/docs/api
http://localhost:5092/swa/api/swagger.json
```

### Add better docs with `<resource>_openapi`

You can enrich generated docs with a companion procedure:

```text
api.Customers_openapi
```

It returns rows describing summaries, descriptions, parameter descriptions, tags, and response descriptions.

Example:

```sql
CREATE OR ALTER PROCEDURE api.Customers_openapi
AS
SELECT
    operation,
    class,
    name,
    property,
    value
FROM (VALUES
    ('*',      'parameter', 'Id',    'description', 'Customer id'),
    ('get',    'operation', '',      'summary',     'Get one or many customers'),
    ('post',   'operation', '',      'summary',     'Create a customer'),
    ('put',    'operation', '',      'summary',     'Update a customer'),
    ('delete', 'operation', '',      'summary',     'Delete a customer'),
    ('*',      'response',  '404',   'description', 'Customer not found')
) AS Data(operation, class, name, property, value);
GO
```

For the full shape, see [`src/UpApi/Docs/ServiceOpenApi.md`](/Users/ole/UpText/Repos/UpApi/src/UpApi/Docs/ServiceOpenApi.md).

## SQL Generator

`UpApi` can generate starter SQL for a table.

Routes:

- `/SqlGenerator`
- `/swa/{service}/sql-generator`

Example:

```bash
curl "http://localhost:5092/swa/api/sql-generator?Table-schema=dbo&Table=Customers&http-verb=all"
```

Supported values for `http-verb`:

- `get`
- `post`
- `put`
- `delete`
- `all`

Optional flags:

- `Paging=true`
- `Sort=true`
- `Search=true`

## Paging, Sorting, And Filtering

If your procedure supports these parameters, `UpApi` knows how to populate them:

- `@first_row`
- `@last_row`
- `@sort_field`
- `@sort_order`
- `@filter`

Example request:

```bash
curl "http://localhost:5092/swa/api/customers?range=[0,24]&sort=[\"Name\",\"ASC\"]&filter=ada"
```

For successful collection `GET` responses, `UpApi` also emits a `Content-Range` header.

## Useful Stored Procedure Conventions

These conventions are recognized by the runtime:

- `@id` is filled from `/resource/{id}` when not present in the query string
- `@requestBody` receives the raw request body
- `@requestHeaders` receives request headers as JSON
- `@url` receives the full request URL
- `@passwordHash` can be derived from an incoming `password`
- `@body` output can replace the normal response payload
- `@total_rows` output can control paging totals
- non-zero SQL return values are used as HTTP status codes

This lets you keep more API behavior inside SQL when needed.

## Binary Responses

Stored procedures can also return files or images.

If a result contains:

- first column named `content_type`
- second column containing bytes

then `UpApi` returns that as a binary HTTP response.

## Why Teams Use This

- Very low ceremony for CRUD APIs
- SQL-centric development model
- Good fit for internal apps, admin tools, and line-of-business systems
- Easy to expose existing database logic as HTTP
- Swagger and JWT come built in instead of as extra projects

## Notes

- This project targets `net10.0`
- It is designed for SQL Server
- The default `appsettings.json` in the repo should be treated as local/dev configuration, not production secrets

## Next Things To Add

Good additions for this project could be:

- a sample database script under `/samples`
- a `docker-compose.yml` with SQL Server and `UpApi`
- a production deployment example
- example login/auth SQL scripts
- integration tests for the CRUD flow
