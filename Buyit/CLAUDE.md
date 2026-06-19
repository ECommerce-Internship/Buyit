# CLAUDE.md

Guidance for Claude Code (and humans) working in the **Buyit** repository. Read this
before making changes — it documents how to build/test/run, the architecture and
dependency rules, and the mandatory patterns every contribution must follow.

---

## 1. What this is

Buyit is an e-commerce backend: an ASP.NET Core **Web API** built on **.NET 10** using
**Clean Architecture**. It covers auth (JWT + Google OAuth), products, categories,
inventory, carts, and orders, with EF Core (SQL Server), Redis caching, Serilog→Seq
logging, and Azure Blob storage.

The solution file is `Buyit.slnx` (the new XML solution format). There is **no `.sln`** —
use `Buyit.slnx` with the `dotnet` CLI (8.0.x+ tooling) or Visual Studio 2022+.

---

## 2. Build, test, run

All commands are run from the solution folder:
`/mnt/c/Users/User/source/repos/Buyit/Buyit` (the folder containing `Buyit.slnx`).

### Build
```bash
dotnet restore Buyit.slnx
dotnet build Buyit.slnx
```

### Test
```bash
# Run the whole test suite
dotnet test Buyit.slnx

# Run a single test class or method
dotnet test --filter "FullyQualifiedName~CategoryServiceTests"
dotnet test --filter "FullyQualifiedName~CategoryServiceTests.CreateCategory_ValidRequest_ReturnsCategory"
```
Tests live in `Buyit.Tests` (xUnit). They use an **in-memory EF Core database**, so they
need **no SQL Server or Docker** to run.

### Run the API
```bash
dotnet run --project Buyit.Api
```
Launch profiles (`Buyit.Api/Properties/launchSettings.json`):
- **http** → `http://localhost:5000`
- **https** → `https://localhost:7001` (+ `http://localhost:5000`)

In **Development** the app, on startup, **applies pending EF migrations and seeds the
database automatically** (see `Program.cs` → `db.Database.Migrate()` + `DbInitializer.Seed`).
So the SQL Server container (section 4) must be running before you start the API.

Useful endpoints in Development:
- Swagger UI: `/swagger`
- OpenAPI JSON: `/openapi/v1.json`

### Configuration / secrets
Connection strings and settings come from `appsettings.json` /
`appsettings.Development.json`. Copy `appsettings.Development.example.json` to
`appsettings.Development.json` and fill in values. Keys the app reads:
- `ConnectionStrings:DefaultConnection` — SQL Server
- `ConnectionStrings:Redis` — Redis (defaults to `localhost:6379`)
- `ConnectionStrings:AzureBlobStorage` — Azure Blob (or Azurite)
- `Jwt:Issuer` / `Jwt:Audience` / `Jwt:Secret` / `Jwt:ExpiryMinutes`
- `Authentication:Google:*` — Google OAuth
- `Seq:ServerUrl` — Seq sink (defaults to `http://localhost:5341`)

**Never commit real secrets.** `appsettings.Development.json` is for local use only.

---

## 3. EF Core migrations

EF Core entities/`DbContext` live in **`Buyit.Infrastructure`**, but the design-time
tooling (`Microsoft.EntityFrameworkCore.Design`) is referenced from **`Buyit.Api`**, which
is the startup project. **Every `dotnet ef` command must therefore pass both projects:**

```bash
# Add a migration (run from the solution folder)
dotnet ef migrations add <MigrationName> \
  --project Buyit.Infrastructure \
  --startup-project Buyit.Api

# Apply migrations to the database manually
dotnet ef database update \
  --project Buyit.Infrastructure \
  --startup-project Buyit.Api

# Remove the last (unapplied) migration
dotnet ef migrations remove \
  --project Buyit.Infrastructure \
  --startup-project Buyit.Api
```

Notes:
- Migrations are stored in `Buyit.Infrastructure/Migrations/`.
- In **Development you usually don't run `database update` by hand** — the API applies
  migrations on startup. Run it manually for non-Development environments.
- Name migrations after the schema change, matching existing history
  (e.g. `AddUserPhoneNumber`, `AddInventoryLastUpdated`).
- If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.

---

## 4. Docker

`docker-compose.yml` (in the solution folder) provides the **backing services only** — the
API itself is run via `dotnet run`/Visual Studio, not in a container.

```bash
docker compose up -d      # start all backing services
docker compose ps         # check status
docker compose down       # stop them
```

Services and ports:

| Service        | Image                                   | Port(s)        | Purpose                          |
| -------------- | --------------------------------------- | -------------- | -------------------------------- |
| `sqlserver`    | `mcr.microsoft.com/mssql/server:2022`   | `1433`         | Primary database                 |
| `redis`        | `redis:7-alpine`                        | `6379`         | Caching (`ICacheService`)        |
| `redisinsight` | `redis/redisinsight:latest`             | `5540`         | Redis GUI (`http://localhost:5540`) |
| `seq`          | `datalust/seq`                          | `5341`→`80`    | Structured logs (`http://localhost:5341`) |

Typical local startup order: `docker compose up -d` → wait for SQL Server → `dotnet run --project Buyit.Api`.

---

## 5. Architecture & the four projects

Clean Architecture. Dependencies point **inward only** — `Domain` is the center and depends
on nothing.

```
        Buyit.Api  ──────────────► Buyit.Application ──────────► Buyit.Domain
            │                              ▲                         ▲
            └──────────► Buyit.Infrastructure ───────────────────────┘
                                  (Infrastructure → Application, Domain)

        Buyit.Tests ──► Buyit.Infrastructure (transitively reaches all layers)
```

### Dependency rules (enforced by project references — do not break these)

| Project              | May reference                       | Must NOT reference                  | Responsibility |
| -------------------- | ----------------------------------- | ----------------------------------- | -------------- |
| **Buyit.Domain**     | *(nothing — no project refs)*       | any other Buyit project             | Entities, enums, constants, **custom exceptions**, pure domain helpers. No EF, no ASP.NET. |
| **Buyit.Application**| `Buyit.Domain`                      | `Infrastructure`, `Api`             | DTOs, service **interfaces**, FluentValidation **validators**, settings POCOs. The contract layer. |
| **Buyit.Infrastructure** | `Buyit.Application`, `Buyit.Domain` | `Api`                           | EF Core `DbContext` + migrations, **service implementations**, external integrations (Redis, Blob, JWT, email). |
| **Buyit.Api**        | `Buyit.Application`, `Buyit.Infrastructure` | —                           | Controllers, middleware, DI wiring (`Program.cs`), extensions. The composition root. |
| **Buyit.Tests**      | `Buyit.Infrastructure`              | —                                   | xUnit unit tests of services. |

Key consequences:
- **Domain stays pure.** Don't add NuGet packages or framework references to it.
- **Interfaces live in `Application`, implementations in `Infrastructure`.** Controllers
  depend on the interface; DI in `Program.cs` binds it to the concrete class.
- **Api is the only composition root.** All `builder.Services.Add...` registrations go in
  `Program.cs`. Adding a new service = create `IFooService` (Application) + `FooService`
  (Infrastructure) + register it in `Program.cs`.

---

## 6. Mandatory patterns

These are how the codebase already works — match them exactly.

### 6.1 Error handling — *throw typed exceptions, never return error codes*

Custom exceptions in `Buyit.Domain/Exceptions/` are the single error-signaling mechanism:

| Exception              | HTTP status | When to throw                          |
| ---------------------- | ----------- | -------------------------------------- |
| `ValidationException`  | 400         | Input failed validation (carries `Errors`) |
| `UnauthorizedException`| 401         | Not authenticated / bad credentials    |
| `ForbiddenException`   | 403         | Authenticated but not allowed          |
| `NotFoundException`    | 404         | Resource does not exist                |
| `ConflictException`    | 409         | Business conflict (e.g. duplicate name)|

Rules:
- **Services throw** these exceptions. They never return null-as-error or status codes.
- **Controllers do NOT try/catch.** A single `ExceptionHandlingMiddleware`
  (`Buyit.Api/Middleware/`) catches everything and converts it to an RFC 7807
  `ProblemDetails` response (`application/problem+json`). `ValidationException.Errors` is
  surfaced under the `errors` extension member.
- Unmapped exceptions become **500** with a generic message (details are logged, not leaked).

```csharp
// In a service — correct:
var category = await _context.Categories.FindAsync(id);
if (category is null)
    throw new NotFoundException($"Category with ID {id} was not found.");
```

### 6.2 Validation — *FluentValidation, invoked inside the service*

- Validators are `AbstractValidator<TRequest>` classes in
  **`Buyit.Application/Validators/`**, one per request DTO.
- They are **registered in `Program.cs`** (`AddScoped<IValidator<T>, TValidator>()`) and
  **injected into the service** that needs them.
- **Validation runs at the start of the service method**, not in the controller and not via
  an MVC filter. On failure, group the errors and throw `ValidationException`:

```csharp
var result = await _createValidator.ValidateAsync(request);
if (!result.IsValid)
{
    var errors = result.Errors
        .GroupBy(e => e.PropertyName)
        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
    throw new ValidationException(errors);
}
```

When you add a request DTO that needs validation: create the validator in `Application`,
register it in `Program.cs`, inject it, and call it first thing in the service method.

### 6.3 DTO placement — *records in Application, entities in Domain*

- **All DTOs live in `Buyit.Application/DTOs/`.** They are `record` types. Naming:
  `...Request` for input, `...Response` (or `...Dto`) for output.
- **Entities live in `Buyit.Domain/Entities/`** and never cross the API boundary.
- **Services do the mapping** entity ↔ DTO (constructing response records by hand — there is
  no AutoMapper). Controllers only pass DTOs in and return DTOs out.
- Don't expose EF entities from controllers; don't put DTOs in Domain or Infrastructure.

### 6.4 Testing style — *xUnit + Moq + FluentAssertions, in-memory EF*

- Framework: **xUnit** (`[Fact]`), assertions with **FluentAssertions** (`.Should()...`),
  mocks with **Moq**.
- One test class per service: `FooServiceTests` in `Buyit.Tests`.
- Test method names: **`Method_Scenario_ExpectedResult`**
  (e.g. `CreateCategory_DuplicateName_ThrowsConflictException`).
- Use a **`BuildSut(...)` helper** that wires up a fresh EF Core **InMemory** database
  (`UseInMemoryDatabase(Guid.NewGuid().ToString())`) per test for isolation, plus mocked
  validators/loggers.
- Validators are mocked to **pass by default**; validation rules are not re-tested in
  service tests.
- Structure each test **Arrange → Act → Assert**. Assert both the returned DTO and the
  persisted state where relevant. For error paths, assert the thrown exception type:
  `await act.Should().ThrowAsync<ConflictException>()`.

---

## 7. Documented inconsistency & chosen standard

**Inconsistency — API route/versioning declaration.** Controllers are not consistent about
how they declare their route and API version:

```csharp
// ProductController — versioned, attribute-based:
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]

// CategoryController — hard-coded version, literal path:
[ApiController]
[Route("api/v1/categories")]      // no [ApiVersion], version baked into the string
```

**Chosen standard → the versioned attribute style** (as in `ProductController`):

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
```

Why: it integrates with `Asp.Versioning` (already wired up via
`AddBuyitApiVersioning()`), keeps the version out of hard-coded strings, and lets the route
template derive the resource name from the controller. New controllers must follow this
form; older controllers (e.g. `CategoryController`) should be migrated to it when touched.

---

## 8. Conventions cheat-sheet

- **Target framework:** `net10.0`, `Nullable` enabled, `ImplicitUsings` enabled across all
  projects — keep new files null-aware.
- **Auth / JWT claims:** `MapInboundClaims = false`, so JWT claims keep **short names**. In
  controllers read `User.FindFirst("sub")` (user id), `"email"`, `"role"` — **not** the
  `ClaimTypes.*` URIs (they return null). See the comment block in `Program.cs`.
- **Logging:** Serilog is the logging system (Console + Seq sinks). Inject
  `ILogger<T>` and log meaningful events in services (see `CategoryService`). Request
  logging is configured in `Program.cs` (4xx → Warning, 5xx → Error).
- **DI registration:** everything is registered in `Program.cs`. Services are `Scoped`;
  `IConnectionMultiplexer` (Redis) and `BlobServiceClient` are `Singleton`.
- **Async all the way:** service and controller methods are `async Task<...>`; EF calls use
  the `...Async` variants.

---

## 9. Adding a new feature (the standard slice)

1. **Domain** — add/adjust entity in `Buyit.Domain/Entities/` (and a custom exception if a
   new error case is needed).
2. **Application** — add request/response **DTO records** in `DTOs/`, a service
   **interface** in `Interfaces/`, and a **validator** in `Validators/` if input needs it.
3. **Infrastructure** — implement the service in `Services/`; if the schema changed, add an
   EF **migration** (section 3).
4. **Api** — add the **controller** (versioned route style, section 7), and **register** the
   service + validator in `Program.cs`.
5. **Tests** — add `FooServiceTests` following the style in section 6.4.
6. Run `dotnet build Buyit.slnx` and `dotnet test Buyit.slnx` before committing.
