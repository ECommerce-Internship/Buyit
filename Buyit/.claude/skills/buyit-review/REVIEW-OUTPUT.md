# Buyit Code Review — Whole-codebase sweep (2026-06-19)

> Produced by the `/buyit-review` skill **v2.0**. **No code was modified.** This is a documented,
> step-by-step remediation guide. Apply fixes yourself in the order in §6.

## 0. Scope & method

- **Mode:** Sweep — all five projects (`Buyit.Api`, `Buyit.Application`, `Buyit.Infrastructure`,
  `Buyit.Domain`, `Buyit.Tests`) plus `docker-compose.yml`, `appsettings*.json`, and git history.
- **Ticket note:** TB-82 names `Shopit.Infrastructure/Repositories/`; "Shopit" is a template
  placeholder (zero occurrences in the repo) — the project is **Buyit**, and
  `Buyit.Infrastructure/Repositories/` is empty (see #18).
- **Depth:** **Full line-by-line read** of every controller and every service
  (Auth, ExternalAuth, Category, Product, Order, Cart, Inventory, Jwt, Cache), the middleware,
  `Program.cs`, `AppDbContext`, `DbInitializer`, all domain exceptions/entities touched, the
  validators, and config. Migrations and `obj/` were read as evidence only.
- **Build/test result:** **NOT RUN** — `dotnet` is not installed in this environment. No finding
  below depends on compilation; the test-coverage gap is reported from file inventory. Please run
  `dotnet build Buyit.slnx` and `dotnet test Buyit.slnx` locally to complete this section.
- **Checklist applied:** sections A–J of `SKILL.md` v2.0.

## 1. Summary table

| # | Severity | Confidence | Area | Location | Finding | OWASP/CWE |
|---|----------|-----------|------|----------|---------|-----------|
| 1 | **Critical** | Confirmed | Security/Auth | `TokenTestController.cs` | Anonymous endpoint mints a valid **Admin** JWT for anyone. | A07 / CWE-287 |
| 2 | **Critical** | Confirmed | Security/Secrets | `appsettings.Development.json` (git history) | JWT secret + Azure key + Google secret were committed to history. | A05 / CWE-798, CWE-540 |
| 3 | **High** | Confirmed | Data/Concurrency | `OrderService.cs:~129` | Stock deducted with no concurrency token → oversell race (txn alone insufficient). | CWE-362 |
| 4 | **High** | Confirmed | Security/Auth | `Program.cs` (no rate limiter) | Login/auth has no rate limiting or account lockout. | A07 / CWE-307 |
| 5 | **High** | Confirmed | Security/Secrets | `appsettings.Development.json` | Live Azure key + Google secret in a working-tree file. | A05 / CWE-798 |
| 6 | **Medium** | Confirmed | API contract | `Cart/Order/AdminOrder/Inventory/Category` controllers | Endpoints missing `[ProducesResponseType]`. | |
| 7 | **Medium** | Confirmed | Security/DoS | `OrderController.GetMyOrders`, `AdminOrderController.GetAllOrders` | Order list `pageSize` is unclamped (unbounded page). | CWE-770 |
| 8 | **Medium** | Confirmed | .NET correctness | whole solution | No `CancellationToken` accepted/forwarded anywhere. | |
| 9 | **Medium** | Likely | .NET correctness | `OrderService.cs:~155` | Confirmation email via `Task.Run` captures scoped services after scope disposal. | CWE-460 |
| 10 | **Medium** | Confirmed | Validation | `AddCartItem`/`UpdateCartItem`/`ApplyCoupon` DTOs | Request DTOs have no validator. | |
| 11 | **Medium** | Confirmed | Validation/Input | `InventoryController` + `InventoryService` | Stock/threshold set from raw `[FromBody] int`, no validation (negatives allowed). | CWE-20 |
| 12 | **Medium** | Confirmed | Consistency/Logging | `JwtTokenService.cs` | Uses static `Serilog.Log` + logs every token mint. | |
| 13 | **Medium** | Confirmed | Scaffolding | `WeatherForecast*`, `TokenTestController` | Template / "REMOVE before merging" code shipped. | |
| 14 | **Medium** | Confirmed | Security/Consistency | `DbInitializer.cs:~18` vs `AuthService.cs` | BCrypt work factor 12 vs default; seeded admin has a known weak password. | CWE-916 |
| 15 | **Medium** | Confirmed | Tests | `Buyit.Tests` | No tests for Order/Cart/Inventory services (incl. the stock race). | |
| 16 | **Low** | Confirmed | API/Consistency | `Category/Order/AdminOrder/Cart/Inventory/ExternalAuth` controllers | Hard-coded route/version vs the `[ApiVersion]` standard. | |
| 17 | **Low** | Confirmed | Consistency | `Infrastructure/Services/*` | Mixed `using`-alias vs fully-qualified `ValidationException`. | |
| 18 | **Low** | Confirmed | Architecture | `Infrastructure/Repositories/` | Empty folder — repository pattern scaffolded but unused. | |
| 19 | **Low** | Confirmed | Security | `ProductController` Import/UploadImage | File type checked by extension only, not content/magic-bytes. | CWE-434 |
| 20 | **Low** | Likely | Security/Robustness | `JwtTokenService`/`Program.cs` | No startup guard that the JWT secret is long enough for HS256. | CWE-326 |

## 2. Findings

### [Critical · Confirmed] #1 — Anonymous endpoint issues an Admin JWT to anyone   *(OWASP A07 / CWE-287)*
- **Where:** `Buyit.Api/Controllers/TokenTestController.cs`, `Generate` action.
- **What:** Decorated only with `[HttpGet("generate")]` — **no `[Authorize]`, no environment gate**:
  ```csharp
  [HttpGet("generate")]
  public IActionResult Generate([FromQuery] UserRole role = UserRole.Admin)
      => Ok(new { token = _jwtTokenService.GenerateAccessToken(new User { Id = 1, Email = "test@buyit.com", Role = role }) });
  ```
  Anyone reaching the API can `GET /api/v1/tokentest/generate?role=Admin` and receive a fully valid,
  correctly-signed admin token. The comment says *"TEMPORARY (TB-26)… REMOVE before merging."*
- **Why it matters (concept):** Complete **authentication bypass / privilege escalation**. Tokens
  must only be minted *after* the server verifies an identity. This single endpoint nullifies the
  strong auth elsewhere.
- **The fix — step by step:**
  1. **Delete** `Buyit.Api/Controllers/TokenTestController.cs`.
  2. Remove any test/script referencing `/tokentest/...`.
  3. Confirm no controller exposes `IJwtTokenService` to the HTTP surface.
- **How to verify:** `GET /api/v1/tokentest/generate` returns 404; Swagger drops the TokenTest group.

### [Critical · Confirmed] #2 — Secrets committed to git history   *(OWASP A05 / CWE-798, CWE-540)*
- **Where:** `Buyit/Buyit.Api/appsettings.Development.json` in commits `5f9586a`, `6027404`,
  `1c9c064` (deleted in `e738557`).
- **What:** `git show 1c9c064:Buyit/Buyit.Api/appsettings.Development.json` still returns a `Jwt:Secret`
  (the file historically also carried the Azure `AccountKey` and Google `ClientSecret`). Git-ignored
  now, but **history is permanent and distributed**.
- **Why it matters (concept):** A leaked JWT signing secret lets an attacker **forge tokens for any
  user/role**; the Azure key grants blob access; the OAuth secret enables app impersonation.
  Deletion ≠ remediation.
- **The fix — step by step:**
  1. **Rotate all three secrets now** (new `Jwt:Secret` ≥ 32 random bytes; rotate Azure storage key;
     reset Google client secret).
  2. Move secrets to `dotnet user-secrets` (dev) and Key Vault/env vars (deployed). Keep only
     non-secret keys in `appsettings.json`.
  3. Keep `appsettings.Development.json` git-ignored (it currently is); keep the committed
     `appsettings.Development.example.json` template with placeholders only.
  4. (Optional) scrub history with `git filter-repo` + coordinated force-push — but rotation is what
     actually protects you.
- **How to verify:** new clones can't read live secrets; app boots from user-secrets/env.

### [High · Confirmed] #3 — Oversell race on stock deduction   *(CWE-362)*
- **Where:** `Buyit.Infrastructure/Services/OrderService.PlaceOrderAsync` — stock checked (~63–72),
  later deducted `item.Product.Inventory!.QuantityInStock -= item.Quantity;` (~129). `Inventory` has
  **no `RowVersion`** and `AppDbContext` configures no concurrency token.
- **What:** The method **does** open a transaction (`BeginTransactionAsync`, good) — but a transaction
  under SQL Server's default READ COMMITTED isolation does **not** stop this race: two concurrent
  orders for the last unit can both read `QuantityInStock = 1`, both pass the check, both subtract,
  and both commit → stock `-1` (lost update / oversell). *(Correction from a prior draft: the
  transaction is already present; adding one is not the fix.)*
- **Why it matters (concept):** Classic **check-then-act race**. Atomicity (transaction) ≠ isolation
  against concurrent readers. You need **optimistic concurrency** or an **atomic conditional update**.
- **The fix — step by step:**
  1. Add a concurrency token to `Inventory`:
     ```csharp
     public byte[] RowVersion { get; set; } = default!;          // Domain/Entities/Inventory.cs
     modelBuilder.Entity<Inventory>().Property(i => i.RowVersion).IsRowVersion();  // AppDbContext
     ```
  2. `dotnet ef migrations add AddInventoryRowVersion --project Buyit.Infrastructure --startup-project Buyit.Api`.
  3. Catch `DbUpdateConcurrencyException` around the existing transaction's save and retry the
     read-check-deduct, **or** deduct atomically
     (`UPDATE Inventories SET QuantityInStock = QuantityInStock - @q WHERE Id=@id AND QuantityInStock >= @q`,
     treating 0 rows affected as insufficient stock — this removes the race without a retry loop).
- **How to verify:** A concurrency test firing two `PlaceOrderAsync` for the last unit asserts exactly
  one succeeds and stock never goes negative.

### [High · Confirmed] #4 — No brute-force protection on authentication   *(OWASP A07 / CWE-307)*
- **Where:** `Program.cs` (no `AddRateLimiter`/`UseRateLimiter`); `AuthController.Login`/`RefreshToken`.
  (The `RateLimiting` strings in `obj/*.json` are transitive framework refs, **not** configured usage.)
- **What:** Login logs failed attempts but applies **no rate limit, throttle, or lockout**. Unlimited
  password guesses can be fired at `/api/v1/auth/login` and `/refresh-token`.
- **Why it matters (concept):** Without rate limiting/lockout, **credential brute-force /
  password-spraying** is trivial. BCrypt slows each guess but does not stop automated volume.
- **The fix — step by step:**
  1. `builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("auth", x => { x.Window = TimeSpan.FromMinutes(1); x.PermitLimit = 5; }));`
     then `app.UseRateLimiter();`
  2. Apply `[EnableRateLimiting("auth")]` to `AuthController` (login/refresh).
  3. Optionally add per-account lockout; key the limiter on IP **and** submitted email.
- **How to verify:** Rapid repeated logins return `429 Too Many Requests` after the threshold.

### [High · Confirmed] #5 — Live secrets in the working-tree dev config   *(OWASP A05 / CWE-798)*
- **Where:** `Buyit.Api/appsettings.Development.json` — real `AzureBlobStorage` `AccountKey`, Google
  `ClientSecret`, `Jwt:Secret`.
- **What:** Same secret set as #2, called out for the **ongoing practice**: live production-grade
  credentials in plaintext in a repo-local file on every dev's disk.
- **Why it matters (concept):** Repo-adjacent secrets leak via backups, screen-shares, support
  bundles, and accidental `git add -f`. Use **user-secrets** (dev) + a secret store (prod).
- **The fix — step by step:**
  1. After rotating (#2): `cd Buyit.Api && dotnet user-secrets init`.
  2. `dotnet user-secrets set "ConnectionStrings:AzureBlobStorage" "<new>"` (repeat for Jwt:Secret,
     Google:ClientSecret, DB connection).
  3. Remove those values from `appsettings.Development.json`, leaving placeholders.
- **How to verify:** Secrets removed from JSON; API still authenticates via user-secrets.

### [Medium · Confirmed] #6 — Endpoints missing `[ProducesResponseType]`
- **Where:** `CartController` (7), `OrderController` (4), `AdminOrderController` (3),
  `InventoryController` (5), `CategoryController` (5) — **zero** `[ProducesResponseType]`.
  `ProductController`, `AuthController`, `ExternalAuthController` annotate fully.
- **What & why:** Violates the Buyit API rule; OpenAPI then advertises only a generic 200, so
  generated clients miss 400/401/403/404/409 and the codebase splits into two styles. The OpenAPI
  document is the contract.
- **The fix (example):**
  ```csharp
  [HttpGet("{id:int}")]
  [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  ```
  Enumerate each action's statuses (success + every exception its service throws + 401/403 if
  `[Authorize]`) and add one attribute per status across the five controllers.
- **How to verify:** `/swagger` lists all response codes/schemas per endpoint.

### [Medium · Confirmed] #7 — Order list pagination is unclamped   *(CWE-770)*
- **Where:** `OrderController.GetMyOrders([FromQuery] int pageSize = 10)` and
  `AdminOrderController.GetAllOrders(... int pageSize = 10 ...)` pass `pageSize` straight to
  `OrderService`, which does `.Take(pageSize)` with **no maximum**.
- **What:** A caller can request `?pageSize=1000000` and force the DB to materialize a huge result
  set. (Contrast `ProductQueryParameters`, which **correctly** clamps `PageSize` to 50 — see §5.)
- **Why it matters (concept):** Unbounded page size is a **resource-exhaustion DoS** vector and a
  consistency gap vs the product listing.
- **The fix — step by step:**
  1. Introduce an `OrderQueryParameters` DTO mirroring `ProductQueryParameters` (private backing
     field + `MaxPageSize` clamp on the `PageSize` setter), or clamp inline in the service:
     `pageSize = Math.Clamp(pageSize, 1, 50);`.
  2. Apply to both `GetMyOrdersAsync` and `GetAllOrdersAsync`.
- **How to verify:** `?pageSize=100000` returns at most the cap.

### [Medium · Confirmed] #8 — No `CancellationToken` anywhere
- **Where:** Whole solution — `grep -r "CancellationToken"` over Api/Application/Infrastructure
  returns nothing.
- **What:** No async path accepts/forwards a token. When a client disconnects or a request times out,
  in-flight EF queries and service work keep running.
- **Why it matters (concept):** **Cooperative cancellation** frees DB connections/threads when work
  is abandoned; without it, slow/abandoned requests waste resources and cap scalability.
- **The fix — step by step:**
  1. Add `CancellationToken ct = default` to service interface methods + implementations.
  2. Forward to EF async calls (`ToListAsync(ct)`, `FirstOrDefaultAsync(.., ct)`, `SaveChangesAsync(ct)`).
  3. Accept `CancellationToken ct` in controller actions (ASP.NET binds `HttpContext.RequestAborted`)
     and pass it down.
- **How to verify:** Build succeeds; cancel a long request and confirm the query stops.

### [Medium · Likely] #9 — Fire-and-forget email captures scoped services after scope disposal   *(CWE-460)*
- **Where:** `OrderService.PlaceOrderAsync` (~line 155):
  ```csharp
  _ = Task.Run(() => _emailService.SendOrderConfirmationAsync(order.Id, userEmail, order.TotalAmount));
  ```
- **What:** `_emailService` is a **scoped** dependency resolved within the request's DI scope. The
  request returns immediately; ASP.NET then disposes the scope (and the scoped `AppDbContext`/any
  scoped deps the email service holds). The detached `Task.Run` may still be running → if it touches
  a disposed scoped dependency it throws `ObjectDisposedException`, and because the task is
  fire-and-forget, **the exception is unobserved** (silent email loss, possible process-level noise).
- **Why it matters (concept):** Background work must not outlive the request scope that owns its
  dependencies. This is the canonical "scoped service captured by a fire-and-forget task" bug.
- **The fix — step by step (pick one):**
  1. **Preferred:** enqueue the email on a background queue (`System.Threading.Channels` +
     `BackgroundService`) that resolves its own scope per item.
  2. **Or** capture an `IServiceScopeFactory`, and inside the task `using var scope =
     factory.CreateScope();` then resolve `IEmailService` from `scope.ServiceProvider`.
  3. **Or** simply `await` the send (simplest; small latency cost) and wrap in try/catch so a mail
     failure doesn't fail the order.
- **How to verify:** Place an order under a simulated mail delay and confirm no
  `ObjectDisposedException`; the email send is logged. *(Likely: exact trigger depends on whether
  `EmailService` uses the scoped `AppDbContext` — verify its implementation.)*

### [Medium · Confirmed] #10 — Missing validators for cart/coupon request DTOs
- **Where:** `AddCartItemRequest`, `UpdateCartItemRequest`, `ApplyCouponRequest` have no `*Validator`.
  `CartService` throws ad-hoc `ValidationException`s inline instead. (Login/Logout/RefreshToken are
  fine without — opaque credentials.)
- **What & why:** The standard is one FluentValidation validator per input DTO, invoked in the
  service — keeping rules declarative, reusable, testable, consistent, and producing the uniform
  `errors` payload. Inline checks drift.
- **The fix:** Add `AddCartItemRequestValidator` (`ProductId > 0`, `Quantity` 1..N) etc., register in
  `Program.cs`, inject + invoke at the top of the `CartService` methods.
- **How to verify:** Unit-test each validator; POST an invalid cart item → 400 with grouped `errors`.

### [Medium · Confirmed] #11 — Inventory stock/threshold set from an unvalidated raw int   *(CWE-20)*
- **Where:** `InventoryController.UpdateStock(int productId, [FromBody] int newQuantity)` and
  `UpdateThreshold(... [FromBody] int newThreshold)` → `InventoryService.UpdateStockAsync` does
  `inventory.QuantityInStock = newQuantity;` with **no guard**.
- **What:** A raw `int` body bypasses the DTO+validator convention, and nothing prevents a **negative**
  quantity/threshold. An admin (or a bug) can set `QuantityInStock = -50`.
- **Why it matters (concept):** Inventory is a non-negative invariant; unvalidated input lets the
  domain enter an impossible state, which then flows into stock checks and the low-stock alert.
- **The fix — step by step:**
  1. Wrap the inputs in DTOs (`UpdateStockRequest { int Quantity }`, `UpdateThresholdRequest`).
  2. Add FluentValidation validators (`GreaterThanOrEqualTo(0)`); register + invoke in the service.
  3. Or, minimally, guard in the service: `if (newQuantity < 0) throw new ValidationException(...)`.
- **How to verify:** `PUT .../stock` with `-5` returns 400; stock never goes negative via this path.

### [Medium · Confirmed] #12 — `JwtTokenService` logs via static `Serilog.Log`
- **Where:** `JwtTokenService.cs` — `using Serilog;` + `Log.Information("Generated JWT access token …")`.
- **What & why:** Every other service injects `ILogger<T>`. The static logger bypasses DI (untestable,
  breaks the abstraction) and emits an info line **per token mint** (Seq noise under load).
- **The fix:** Inject `ILogger<JwtTokenService>`; use `_logger.LogDebug(...)`; remove `using Serilog;`.
- **How to verify:** Build passes; no per-call info log; a `Mock<ILogger<>>` works in tests.

### [Medium · Confirmed] #13 — Template / temporary scaffolding still shipped
- **Where:** `WeatherForecastController.cs`, `WeatherForecast.cs`, `TokenTestController.cs` (also #1).
  The only `DateTime.Now` in the codebase is in `WeatherForecastController` — it disappears with this removal.
- **What & why:** Dead/sample endpoints enlarge attack surface, pollute Swagger, confuse readers.
- **The fix:** Delete all three; grep for `WeatherForecast`/`tokentest` and remove references.
- **How to verify:** Build passes; Swagger shows only real feature groups.

### [Medium · Confirmed] #14 — Inconsistent BCrypt work factor + weak seeded admin   *(CWE-916)*
- **Where:** `AuthService` uses `BCrypt.HashPassword(pw, 12)`; `DbInitializer.Seed` uses
  `BCrypt.HashPassword("Admin123!")` (default factor 11) for `admin@buyit.com`.
- **What & why:** Two cost factors for one concern, plus a seeded admin with a public weak password.
- **The fix:** Define `const int BcryptWorkFactor = 12;` once and use everywhere; read the seed admin
  password from config/user-secrets (or force reset-on-first-login). Keep seeding Dev-only (it is).
- **How to verify:** All `HashPassword` calls reference the shared constant; no literal admin password.

### [Medium · Confirmed] #15 — Test coverage gaps on the highest-risk services
- **Where:** `Buyit.Tests` has only `AuthServiceTests`, `CategoryServiceTests`,
  `ExternalAuthServiceTests`, `ProductServiceTests`. **No tests** for `OrderService`, `CartService`,
  `InventoryService`, `JwtTokenService`, `CacheService`.
- **What & why:** The untested services hold the most business logic — including the **stock path
  (#3)**, exactly where a regression test belongs. Untested money/stock logic is a liability.
- **The fix:** Add xUnit tests (`BuildSut` + EF InMemory) for Order/Cart/Inventory covering happy and
  throw paths; include an oversell/concurrency test for #3.
- **How to verify:** `dotnet test` shows new passing tests.

### [Low · Confirmed] #16 — Route/versioning inconsistency (codebase-wide)
- **Where:** `CategoryController`, `OrderController`, `AdminOrderController`, `CartController`,
  `InventoryController` all use hard-coded `[Route("api/v1/...")]` with **no `[ApiVersion]``;
  `ExternalAuthController` uses `api/auth/...`. Only `Product`/`Auth`/`TokenTest`/`Weather` use the
  versioned template.
- **Fix:** Migrate to `[ApiVersion("1.0")]` + `[Route("api/v{version:apiVersion}/[controller]")]`
  (CLAUDE.md §7); update clients/tests. **Verify:** routes resolve via the versioned template.

### [Low · Confirmed] #17 — Mixed exception-reference style
- **Where:** `AuthService`/`ProductService` use a `using` alias; `CategoryService`/`CartService`/
  `OrderService` fully-qualify `Buyit.Domain.Exceptions.ValidationException`.
- **Fix:** Standardize on `using Buyit.Domain.Exceptions;` + short name. **Verify:**
  `grep -r "Buyit.Domain.Exceptions.ValidationException" Buyit.Infrastructure` is empty.

### [Low · Confirmed] #18 — Empty `Repositories/` folder (unused pattern)
- **Where:** `Buyit.Infrastructure/Repositories/` = `.gitkeep` only; `.csproj` has
  `<Folder Include="Repositories\" />`. All data access is in `Services` via `AppDbContext`.
- **Fix (decide deliberately):** delete the folder + `<Folder>` entry and note in CLAUDE.md that
  services are the data layer; **or** adopt real `IXxxRepository` types. **Verify:** folder gone or populated.

### [Low · Confirmed] #19 — File uploads validated by extension only   *(CWE-434)*
- **Where:** `ProductController.Import` (.xlsx) and `UploadImage` (.jpg/.jpeg/.png) check
  `Path.GetExtension` + size only.
- **Fix:** Add magic-byte/content-type validation (verify PNG/JPEG signatures; EPPlus fails closed for
  xlsx); consider server-side image re-encoding. **Verify:** a text file renamed `.png` is rejected.

### [Low · Likely] #20 — No startup guard on JWT secret strength   *(CWE-326)*
- **Where:** `JwtTokenService`/`Program.cs` build the signing key directly from `jwtSettings.Secret`.
- **Fix:** At startup validate `Secret` present and `Encoding.UTF8.GetByteCount(secret) >= 32`
  (HS256 needs ≥ 256-bit) via Options validation with `ValidateOnStart`. **Verify:** short secret →
  immediate descriptive startup failure. *(Likely: depends on the configured secret length at runtime.)*

## 3. Nits (grouped)
- `docker-compose.yml`: `MSSQL_SA_PASSWORD` hard-coded **and** the dev connection string points at
  `(localdb)\MSSQLLocalDB`, so the compose SQL Server container is unused — move the password to a
  `.env` and align the connection string (or drop the service and document LocalDB in CLAUDE.md §4).
- `AuthController.Register` returns `200 OK`; creating an account is arguably `201 Created`.
- `OrderController`/`CartController` are restricted to `[Authorize(Roles="Customer")]`, so an Admin
  account cannot place orders or use a cart — confirm this is intended (it's restrictive, not a hole).

## 4. Files reviewed with no findings
- `Buyit.Api/Middleware/ExceptionHandlingMiddleware.cs` — correct ProblemDetails mapping.
- `Buyit.Api/Controllers/ProductController.cs`, `AuthController.cs`, `ExternalAuthController.cs` — full
  `[ProducesResponseType]` coverage; ExternalAuth implements anti-CSRF state + ID-token validation.
- `Buyit.Domain/Exceptions/*`, `Buyit.Domain/Common/EmailNormalizer.cs`.
- `Buyit.Infrastructure/Data/AppDbContext.cs` — deliberate cascade/restrict, decimal precision, soft-delete filter.
- `Buyit.Infrastructure/Services/CategoryService.cs`, `ExternalAuthService.cs`, `InventoryService.cs`
  (logic clean aside from #11's input validation), `CacheService.cs` (exemplary fail-open).
- `Buyit.Application/Validators/*` (the 9 present validators); `ProductQueryParameters.cs`,
  `PaginatedResult.cs` — pagination correctly clamped.

## 5. What's already done well (preserve these)
- **Strong auth hardening:** generic login errors with a user-enumeration guard for Google-only
  accounts; refresh-token rotation; **revoke-all-sessions on password change**; CSPRNG refresh tokens.
- **Careful OAuth:** `ExternalAuthController` sets an HttpOnly/Secure/SameSite **anti-CSRF state
  cookie**, validates it, exchanges the code server-side, and validates the Google ID token's audience.
- **Authorization/ownership enforced:** `OrderService.GetOrderByIdAsync`/`CancelOrderAsync` check
  `order.UserId == userId` (or admin) → no IDOR.
- **Resilient caching:** `CacheService` is **fail-open** — Redis faults degrade to a DB hit, never a 500.
- **Solid EF modeling:** `HasPrecision(18,2)` on money, deliberate cascade/restrict (incl. self-cascade
  avoidance), global soft-delete filter; `PlaceOrderAsync` wraps writes in a transaction.
- **Pagination done right for products** (`PageSize` clamped to 50); **no sync-over-async** anywhere;
  **UTC** timestamps throughout (except the scaffolding being removed).
- **Clean test pattern:** isolated EF InMemory DB per test, `BuildSut`, Moq + FluentAssertions.

## 6. Prioritized action list
1. **[Critical]** Delete `TokenTestController` (#1).
2. **[Critical]** Rotate JWT secret, Azure key, Google secret; move to user-secrets/Key Vault (#2, #5).
3. **[High]** Add `Inventory.RowVersion`; make stock deduction race-safe (atomic update / retry) (#3).
4. **[High]** Add rate limiting / lockout on auth endpoints (#4).
5. **[Medium]** Clamp order pagination (#7); add `[ProducesResponseType]` to the 5 controllers (#6).
6. **[Medium]** Fix the fire-and-forget email scope bug (#9); thread `CancellationToken` (#8).
7. **[Medium]** Add cart/coupon validators (#10) and inventory input validation (#11).
8. **[Medium]** Switch `JwtTokenService` to `ILogger<T>` (#12); remove WeatherForecast scaffolding (#13).
9. **[Medium]** Centralize BCrypt factor + de-hardcode seed admin (#14); add Order/Cart/Inventory tests (#15).
10. **[Low]** Standardize routing/versioning (#16) and exception style (#17); decide on Repositories (#18).
11. **[Low]** Harden upload type checks (#19); add JWT-secret length guard (#20).
12. **[Nit]** Fix docker SA-password + connection-string mismatch; review Register status code & role gating (§3).

---
*End of review. No files were modified. Build/test not run (dotnet unavailable) — run `dotnet build Buyit.slnx` and `dotnet test Buyit.slnx` locally to complete §0.*
