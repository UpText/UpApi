# UpApi

`UpApi` turns SQL Server stored procedures into a REST API.

You define your API in SQL, not in controllers:

- Create stored procedures like `customers_get`, `customers_post`, `customers_put`, and `customers_delete`
- Point `UpApi` at a SQL Server database and schema
- Run the app from source or as a container
- Get REST endpoints, JWT protection, OpenAPI, and Swagger UI automatically

It is built for teams that want a simple SQL-first API layer with very little moving code.

## What It Gives You

- SQL-first REST API generation from stored procedures
- SQL Server support
- All resources are defined in SQL
- Run in Docker as a container
- Run from source with `dotnet run`
- Automatic OpenAPI generation per service
- Built-in Swagger UI
- JWT token creation for login endpoints
- JWT-based endpoint protection using SQL parameter naming
- Paging and sorting support through stored procedure parameters
- Binary/file responses from SQL
- Multi-service support through configuration

## How It Works

`UpApi` maps HTTP verbs to stored procedure names:

- `GET /swa/api/customers` -> `api.customers_get`
- `GET /swa/api/customers/1` -> `api.customers_get @id = 1`
- `POST /swa/api/customers` -> `api.customers_post`
- `PUT /swa/api/customers/1` -> `api.customers_put @id = 1`
- `DELETE /swa/api/customers/1` -> `api.customers_delete @id = 1`

The `api` part part of the request refers to a service definition in the configuration. Each service points to:

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

Thus many api's can be hosted by a single container. Also the service user should only have exec permission on the api schema. 

## Requirements

- SQL Server
- Docker, if you want to run the container image
- .NET 10 SDK to run from source

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

- `api.customers_get`
- `api.customers_post`
- `api.customers_put`
- `api.customers_delete`
- `api.customers_openapi` optional, for richer docs

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

## Complete CRUD Demo

This demo is written for a DBA working in SQL Server Management Studio (SSMS). At the end, you will have:

- a new SQL Server database
- an `api` schema that holds the REST-facing stored procedures
- an `upservice` login and user with minimal permissions
- a `dbo.customers` table with demo data
- CRUD stored procedures for `Customer`
- an `UpApi` container running from Docker Hub

After the container starts, the first URL to try is:

- [http://localhost:5092/swa/api/customers](http://localhost:5092/swa/api/customers)

Then open Swagger UI for the same demo service:

- [http://localhost:5092/docs/api](http://localhost:5092/docs/api)

The generated OpenAPI document for the demo service is here:

- [http://localhost:5092/swa/api/swagger.json](http://localhost:5092/swa/api/swagger.json)

### 1. Create the demo database in SSMS

Open a new query window in SSMS as a sufficiently privileged login such as `sa`, then run the full script below. It creates everything needed for the demo in one go.

```sql
USE master;
GO

IF DB_ID('UpApiDemo') IS NULL
BEGIN
    CREATE DATABASE UpApiDemo;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'upservice')
BEGIN
    CREATE LOGIN upservice WITH PASSWORD = 'VerySecret!321';
END;
GO

USE UpApiDemo;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'api')
BEGIN
    EXEC('CREATE SCHEMA api AUTHORIZATION dbo;');
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'upservice')
BEGIN
    CREATE USER upservice FOR LOGIN upservice;
END;
GO

GRANT CONNECT TO upservice;
GRANT EXECUTE ON SCHEMA::[api] TO upservice;
GO

IF OBJECT_ID('dbo.customers', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.customers;
END;
GO

CREATE TABLE dbo.customers
(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name nvarchar(100) NOT NULL,
    Email nvarchar(200) NULL,
    City nvarchar(100) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_customers_CreatedAt DEFAULT SYSUTCDATETIME()
);
GO

INSERT INTO dbo.customers (Name, Email, City)
VALUES
    ('Ada Lovelace', 'ada@example.com', 'London'),
    ('Grace Hopper', 'grace@example.com', 'New York'),
    ('Linus Torvalds', 'linus@example.com', 'Helsinki');
GO

CREATE OR ALTER PROCEDURE api.customers_get
    @Id varchar(max) = NULL,
    @first_row int = NULL,
    @last_row int = NULL,
    @sort_field nvarchar(128) = NULL,
    @sort_order nvarchar(4) = NULL,
    @filter nvarchar(max) = NULL,
    @total_rows int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdInt int = TRY_CAST(@Id AS int);
    DECLARE @SortField nvarchar(128) = LOWER(COALESCE(@sort_field, 'id'));
    DECLARE @SortOrder nvarchar(4) = UPPER(COALESCE(@sort_order, 'ASC'));
    DECLARE @Offset int = CASE WHEN @first_row IS NULL OR @first_row < 0 THEN 0 ELSE @first_row END;
    DECLARE @Fetch int = CASE
        WHEN @last_row IS NULL OR @last_row < @Offset THEN 2147483647
        ELSE (@last_row - @Offset) + 1
    END;

    ;WITH Filtered AS
    (
        SELECT
            Id,
            Name,
            Email,
            City,
            CreatedAt
        FROM dbo.customers
        WHERE (@IdInt IS NULL OR Id = @IdInt)
          AND (
                @filter IS NULL
                OR Name LIKE '%' + @filter + '%'
                OR Email LIKE '%' + @filter + '%'
                OR City LIKE '%' + @filter + '%'
              )
    )
    SELECT @total_rows = COUNT(*) FROM Filtered;

    SELECT
        Id,
        Name,
        Email,
        City,
        CreatedAt
    FROM Filtered
    ORDER BY
        CASE WHEN @SortField = 'name' AND @SortOrder = 'ASC' THEN Name END ASC,
        CASE WHEN @SortField = 'name' AND @SortOrder = 'DESC' THEN Name END DESC,
        CASE WHEN @SortField = 'email' AND @SortOrder = 'ASC' THEN Email END ASC,
        CASE WHEN @SortField = 'email' AND @SortOrder = 'DESC' THEN Email END DESC,
        CASE WHEN @SortField = 'city' AND @SortOrder = 'ASC' THEN City END ASC,
        CASE WHEN @SortField = 'city' AND @SortOrder = 'DESC' THEN City END DESC,
        CASE WHEN @SortField = 'createdat' AND @SortOrder = 'ASC' THEN CreatedAt END ASC,
        CASE WHEN @SortField = 'createdat' AND @SortOrder = 'DESC' THEN CreatedAt END DESC,
        CASE WHEN @SortField = 'id' AND @SortOrder = 'DESC' THEN Id END DESC,
        Id ASC
    OFFSET @Offset ROWS
    FETCH NEXT @Fetch ROWS ONLY;

    RETURN 200;
END;
GO

CREATE OR ALTER PROCEDURE api.customers_post
    @Name nvarchar(100),
    @Email nvarchar(200) = NULL,
    @City nvarchar(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.customers (Name, Email, City)
    VALUES (@Name, @Email, @City);

    DECLARE @NewId int = SCOPE_IDENTITY();

    EXEC api.customers_get @Id = @NewId;
    RETURN 200;
END;
GO

CREATE OR ALTER PROCEDURE api.customers_put
    @Id varchar(max),
    @Name nvarchar(100) = NULL,
    @Email nvarchar(200) = NULL,
    @City nvarchar(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdInt int = TRY_CAST(@Id AS int);

    IF @IdInt IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.customers WHERE Id = @IdInt)
    BEGIN
        RAISERROR('Unknown customer', 1, 1);
        RETURN 404;
    END;

    UPDATE dbo.customers
    SET
        Name = COALESCE(@Name, Name),
        Email = COALESCE(@Email, Email),
        City = COALESCE(@City, City)
    WHERE Id = @IdInt;

    EXEC api.customers_get @Id = @IdInt;
    RETURN 200;
END;
GO

CREATE OR ALTER PROCEDURE api.customers_delete
    @Id varchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdInt int = TRY_CAST(@Id AS int);

    IF @IdInt IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.customers WHERE Id = @IdInt)
    BEGIN
        RAISERROR('Unknown customer', 1, 1);
        RETURN 404;
    END;

    DELETE FROM dbo.customers
    WHERE Id = @IdInt;

    RETURN 200;
END;
GO

CREATE OR ALTER PROCEDURE api.customers_openapi
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        operation,
        class,
        name,
        property,
        value
    FROM (VALUES
        ('get',    'operation', '',           'summary',     'Get one customer or a paged list of customers'),
        ('post',   'operation', '',           'summary',     'Create a customer'),
        ('put',    'operation', '',           'summary',     'Update a customer'),
        ('delete', 'operation', '',           'summary',     'Delete a customer'),
        ('*',      'parameter', 'Id',         'description', 'Customer id from the route'),
        ('get',    'parameter', 'filter',     'description', 'Searches Name, Email, and City'),
        ('get',    'parameter', 'sort',       'description', 'Example: [\"Name\",\"ASC\"]'),
        ('get',    'parameter', 'range',      'description', 'Example: [0,24] for server-side paging'),
        ('*',      'response',  '404',        'description', 'Customer not found')
    ) AS Data(operation, class, name, property, value);
END;
GO
```

### 2. Verify the database objects in SSMS

You should now see:

- database `UpApiDemo`
- schema `api`
- table `dbo.customers`
- procedures `api.customers_get`, `api.customers_post`, `api.customers_put`, `api.customers_delete`, and `api.customers_openapi`
- database user `upservice`

You can also validate the seed data directly in SSMS:

```sql
USE UpApiDemo;
GO

SELECT * FROM dbo.customers ORDER BY Id;
GO
```

### 3. Start the container from Docker Hub

Run `UpApi` as a container with a minimal configuration that only defines the `api` service:

```bash
docker run --rm \
  -p 5092:8080 \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e JWT_SECRET=replace-with-a-long-random-secret \
  -e JWT_ISSUER=UpApi \
  -e JWT_AUDIENCE=UpApiClient \
  -e JWT_HOURS=8 \
  -e Services__api__SqlSchema=api \
  -e Services__api__SqlConnectionString="Server=host.docker.internal,1433;Initial Catalog=UpApiDemo;User ID=upservice;Password=VerySecret!321;Encrypt=False;TrustServerCertificate=True;" \
  uptext/upapi
```

Notes:

- `host.docker.internal` lets the container connect back to SQL Server running on your machine
- if SQL Server is on another host, replace `host.docker.internal` with that server name
- only one service is configured here: `api`

### 4. Open the demo endpoints

First, list the customers:

- [http://localhost:5092/swa/api/customers](http://localhost:5092/swa/api/customers)

That endpoint should return the seed rows from `dbo.customers`.

Then open Swagger UI for the demo service:

- [http://localhost:5092/docs/api](http://localhost:5092/docs/api)

Swagger UI lets you inspect and execute the demo CRUD operations against:

- `GET /swa/api/customers`
- `GET /swa/api/customers/{id}`
- `POST /swa/api/customers`
- `PUT /swa/api/customers/{id}`
- `DELETE /swa/api/customers/{id}`

The service-specific OpenAPI document is also available here:

- [http://localhost:5092/swa/api/swagger.json](http://localhost:5092/swa/api/swagger.json)

### 5. Optional command-line tests

Create a customer:

```bash
curl -X POST http://localhost:5092/swa/api/customers \
  -H "Content-Type: application/json" \
  -d '{"name":"Margaret Hamilton","email":"margaret@example.com","city":"Boston"}'
```

Update customer `1`:

```bash
curl -X PUT http://localhost:5092/swa/api/customers/1 \
  -H "Content-Type: application/json" \
  -d '{"city":"Oslo"}'
```

Delete customer `3`:

```bash
curl -X DELETE http://localhost:5092/swa/api/customers/3
```

### Summary

You have created a REST API for CRUD operations on a `Customer` resource with SQL Server objects that a DBA can own and maintain.

You can extend the demo with:

- authentication by adding procedures such as `api.login_post` and protected parameters such as `@auth_userid`
- server-side paging with `@first_row`, `@last_row`, and `@total_rows`
- sorting with `@sort_field` and `@sort_order`
- searching with `@filter`
- SQL logging to an `api.log` table for request and response auditing
- CORS configuration through `Cors:AllowedOrigins` or matching environment variables

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
api.customers_openapi
```

It returns rows describing summaries, descriptions, parameter descriptions, tags, and response descriptions.

Example:

```sql
CREATE OR ALTER PROCEDURE api.customers_openapi
AS
SELECT
    operation,
    class,
    name,
    property,
    value
FROM (VALUES
    ('*',      'parameter', 'Id',    'description', 'customer id'),
    ('get',    'operation', '',      'summary',     'Get one or many customers'),
    ('post',   'operation', '',      'summary',     'Create a customer'),
    ('put',    'operation', '',      'summary',     'Update a customer'),
    ('delete', 'operation', '',      'summary',     'Delete a customer'),
    ('*',      'response',  '404',   'description', 'customer not found')
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
curl "http://localhost:5092/swa/api/sql-generator?Table-schema=dbo&Table=customers&http-verb=all"
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
