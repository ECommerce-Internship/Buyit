# CLAUDE.md — Buyit

Guidance for Claude Code when working in this repository. This file loads automatically
at the start of every session, so I already know the project without being re-told.

## What this is
**Buyit** — an e-commerce REST API built in .NET 10 using Clean Architecture.

## Solution layout (`Buyit.slnx`)
Dependency direction: **Domain ← Application ← Infrastructure ← Api**

- **Buyit.Domain** — core, no dependencies. Entities, Enums, Exceptions.
  - Entities: User, RefreshToken, Category, Product, Inventory, Coupon, Cart, CartItem,
    Order, OrderItem, Payment, Review
  - Enums: OrderStatus, PaymentMethod, PaymentStatus, UserRole
  - Exceptions: Conflict, Forbidden, NotFound, Unauthorized, Validation
- **Buyit.Application** — references Domain. Holds `DTOs/` and `Interfaces/` (currently empty — to be built).
- **Buyit.Infrastructure** — references Application + Domain. EF Core data access.
  - `Data/AppDbContext.cs` — DbContext, all entity relationships, constraints, soft-delete filter
  - `Data/DbInitializer.cs` — seed data
  - `Migrations/` — EF Core migrations
  - `Repositories/` — currently empty (to be built)
  - Packages: EF Core SqlServer, BCrypt.Net-Next (password hashing)
- **Buyit.Api** — references Application + Infrastructure. ASP.NET Core entry point.
  - `Program.cs`, `Controllers/`, `Middleware/ExceptionHandlingMiddleware.cs`, `Extensions/`
  - Swagger/OpenAPI enabled; permissive `DevCors` policy in Development only

## Database
- SQL Server **LocalDB**, database name `BuyitDb`
- Connection string in `Buyit.Api/appsettings.Development.json` (`DefaultConnection`)
- On startup in Development, `Program.cs` runs `db.Database.Migrate()` then `DbInitializer.Seed(db)`
- Key model rules (see `AppDbContext.OnModelCreating`):
  - Unique indexes: User.Email, Product.Sku, Coupon.Code
  - Decimal precision (18,2) on money fields; (5,2) on Coupon.DiscountPercentage
  - Check constraint: Review.Rating BETWEEN 1 AND 5
  - Global query filter: Product excludes `IsDeleted` rows (use `.IgnoreQueryFilters()` to see them)

## Common commands
Run from the `Buyit.Api` project directory unless noted.
- Build:           `dotnet build`
- Run the API:     `dotnet run --project Buyit.Api`   (Swagger at `/swagger` in Development)
- Add migration:   `dotnet ef migrations add <Name> --project Buyit.Infrastructure --startup-project Buyit.Api`
- Apply migration: `dotnet ef database update --project Buyit.Infrastructure --startup-project Buyit.Api`

## Conventions
- C# nullable + implicit usings enabled across all projects
- Errors flow through `ExceptionHandlingMiddleware` (maps domain exceptions → HTTP status codes)
- Keep the dependency direction intact: Domain must not reference any other project

## Current status / next steps
- Domain entities + DB schema: **done** (2 migrations applied)
- Still scaffolding: default `WeatherForecastController` is placeholder; `Interfaces`, `DTOs`,
  `Repositories` are empty. Next work is building out repositories, services, DTOs, and real controllers.

## How I should work in this repo
- I can read any file under this folder directly — no need for the user to paste paths.
- For "what's the progress / what's next", check this file's status section + recent git log + the
  empty folders above, then propose the next step.
