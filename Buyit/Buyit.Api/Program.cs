using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Azure.Storage.Blobs;
using Buyit.Api.Extensions;
using Buyit.Api.Middleware;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Application.Validators;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Mcp;
using Buyit.Infrastructure.Services;
using Buyit.Infrastructure.Workers;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using OfficeOpenXml;
using Resend;
using Serilog;
using StackExchange.Redis;


// Bootstrap logger — captures startup errors before the host config is loaded
Serilog.Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
ExcelPackage.License.SetNonCommercialPersonal("Carl Ibrahim");

// Replace default .NET logging with Serilog (Console + SEQ sinks)
builder.Host.UseSerilog((context, services, config) =>
{
    config
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Seq(
        context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341",
        apiKey: context.Configuration["Seq:ApiKey"]);
});

// Register Services
builder.Services.AddControllers();              // enables controller-based endpoints

builder.Services.AddOpenApi();

// swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddBuyitApiVersioning();
builder.Services.AddBuyitSwagger();

// CORS Policy
const string DevCors = "DevCors";
const string ProdCors = "ProdCors";
// Production origins come from config so the deployed SPA (e.g. the Vercel URL) is allowed.
// Set them in appsettings.Production.json or an env var (Cors__AllowedOrigins__0=https://...).
// AllowCredentials is required because the refresh-token cookie rides on cross-site XHRs.
var prodCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCors, policy =>
        policy.WithOrigins(["http://localhost:5173", "https://localhost:5173", .. prodCorsOrigins])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());

    options.AddPolicy(ProdCors, policy =>
        policy.WithOrigins(prodCorsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// EF Core — register the database context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector()));   // TB-156: Pgvector.EntityFrameworkCore — maps the SQL "vector" type
// JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<GoogleAuthSettings>(
    builder.Configuration.GetSection("Authentication:Google"));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IExternalAuthService, ExternalAuthService>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
// --- TB-123/124/125: Seller side (stores, seller registration, ownership) ---
builder.Services.AddScoped<IStoreService, StoreService>();
builder.Services.AddScoped<IValidator<RegisterSellerRequest>, RegisterSellerRequestValidator>();
builder.Services.AddScoped<IValidator<CreateStoreRequest>, CreateStoreRequestValidator>();
builder.Services.AddHttpContextAccessor();   // lets CurrentUserService read the request's claims
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IValidator<CreateCategoryRequest>, CreateCategoryRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateCategoryRequest>, UpdateCategoryRequestValidator>();
// TB-76: lets the Google auth controller make a server-to-server call to Google's token endpoint
builder.Services.AddHttpClient();

// Service Registration 
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IValidator<UpdateProfileRequest>, UpdateProfileRequestValidator>();
builder.Services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();
builder.Services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<PlaceOrderRequest>, PlaceOrderRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateOrderStatusRequest>, UpdateOrderStatusRequestValidator>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IOrderService, OrderService>();
// --- TB-40: Payment feature registrations ---
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IValidator<ProcessPaymentRequest>, ProcessPaymentRequestValidator>();
// --- TB-41: Review feature registrations ---
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IValidator<SubmitReviewRequest>, SubmitReviewRequestValidator>();

// Register email service — priority: Gmail API (prod) > Resend (fallback) > Ethereal (dev) > placeholder
// Gmail API and Resend both send over HTTPS, not raw SMTP, so neither is blocked by Render's
// outbound SMTP port restrictions the way Ethereal/Gmail SMTP is.
builder.Services.Configure<EtherealSettings>(builder.Configuration.GetSection("Ethereal"));
builder.Services.Configure<ResendSettings>(builder.Configuration.GetSection("Resend"));
builder.Services.Configure<GmailApiSettings>(builder.Configuration.GetSection("GmailApi"));
var gmailApiRefreshToken = builder.Configuration["GmailApi:RefreshToken"];
var resendApiKey = builder.Configuration["Resend:ApiKey"];
var etherealUsername = builder.Configuration["Ethereal:Username"];
if (!string.IsNullOrWhiteSpace(gmailApiRefreshToken))
{
    builder.Services.AddScoped<IEmailService, GmailApiEmailService>();
}
else if (!string.IsNullOrWhiteSpace(resendApiKey))
{
    builder.Services.AddResend(o => { o.ApiToken = resendApiKey; });
    builder.Services.AddScoped<IEmailService, ResendEmailService>();
}
else if (!string.IsNullOrWhiteSpace(etherealUsername))
    builder.Services.AddScoped<IEmailService, EtherealEmailService>();
else
    builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.Configure<SftpSettings>(builder.Configuration.GetSection("Sftp"));
builder.Services.AddScoped<ISftpImportService, SftpImportService>();
// --- TB-32: Product feature registrations ---
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IValidator<CreateProductRequest>, CreateProductRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateProductRequest>, UpdateProductRequestValidator>();
// TB-47: validator for the product generate-content endpoint.
builder.Services.AddScoped<IValidator<GenerateContentRequest>, GenerateContentRequestValidator>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ILowStockAlertService, LowStockAlertService>();
builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<IConversationStore, RedisConversationStore>();

// --- TB-43: Azure Queue registrations ---
builder.Services.Configure<AzureQueueSettings>(builder.Configuration.GetSection("AzureQueue"));
builder.Services.AddHostedService<LowStockWorker>();
// --- TB-46: Gemini AI product-content feature registrations ---
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));

builder.Services.AddHttpClient("GeminiClient", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IValidator<GenerateProductContentRequest>, GenerateProductContentRequestValidator>();
// TB-156: semantic-search embedding client (reuses the "GeminiClient" HttpClient above).
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// --- TB-97: AI chatbot (Gemini <-> Buyit.MCP function-calling bridge) ---
builder.Services.Configure<McpSettings>(builder.Configuration.GetSection("Mcp"));
builder.Services.Configure<ChatHistorySettings>(builder.Configuration.GetSection("ChatHistory"));
// TB-103: pooled HttpClient for the MCP HTTP transport. A factory-managed client reuses its
// socket handler across chat messages (avoids per-request HttpClient socket exhaustion).
builder.Services.AddHttpClient(McpConnector.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IMcpConnector, McpConnector>();
builder.Services.AddScoped<IValidator<ChatRequest>, ChatRequestValidator>();
builder.Services.AddScoped<IChatService, ChatService>();

//TB:158 forgot password reset email service
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
// TB-157: Coupon CRUD (Admin global, Seller store-scoped)
builder.Services.AddScoped<ICouponService, CouponService>();
builder.Services.AddScoped<IValidator<CreateCouponRequest>, CreateCouponRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateCouponRequest>, UpdateCouponRequestValidator>();

// Each chat message opens an HTTP session to the MCP service (TB-103) and calls the paid Gemini
// API, so throttle the "chat" endpoint PER USER (not per server): a sliding window of 10 requests/minute keyed on the
// caller's "sub" claim, no queue (excess -> 429). Uses AddPolicy so we can partition by user —
// AddSlidingWindowLimiter alone would create a single shared bucket. No new libraries: this is
// the built-in System.Threading.RateLimiting / Microsoft.AspNetCore.RateLimiting.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // On a 429, tell the client when it may retry (RFC 9110 §10.2.3) so well-behaved callers
    // back off instead of hammering. The sliding-window limiter exposes this via lease metadata.
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        return ValueTask.CompletedTask;
    };

    options.AddPolicy("chat", httpContext =>
    {
        // One bucket per authenticated user. This limiter runs AFTER UseAuthorization (see the
        // pipeline below), so "sub" is always populated for chat; fall back to the client IP
        // (then a constant) so a missing key can never merge all callers into a shared bucket.
        var partitionKey = httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,                       // 10 requests...
                Window = TimeSpan.FromMinutes(1),       // ...per rolling minute...
                SegmentsPerWindow = 6,                  // ...checked in 10-second slices (smooth)
                QueueLimit = 0                          // no waiting room: excess -> immediate 429
            });
    });

    // TB-156: semantic product search also calls the paid Gemini (embedding) API, so it still
    // needs a throttle — but a MUCH more generous one than "chat", because a shopper naturally
    // fires many searches in a browsing session (refining terms, paging categories). 60/minute
    // (~1 per second) keeps normal browsing unhindered while still capping scripted abuse. This
    // endpoint is public, so the partition falls back to the client IP when there's no "sub".
    options.AddPolicy("semantic-search", httpContext =>
    {
        var partitionKey = httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,                       // 60 searches...
                Window = TimeSpan.FromMinutes(1),       // ...per rolling minute (~1/sec)...
                SegmentsPerWindow = 6,                  // ...smoothed over 10-second slices...
                QueueLimit = 0                          // excess -> immediate 429 (with Retry-After)
            });
    });

    options.AddPolicy("forgot-password", httpContext =>
    {
        // This endpoint is [AllowAnonymous] — there's no "sub" claim to key on, so partition by
        // IP instead, so one abusive client can't email-bomb arbitrary addresses.
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 3,                      // 3 requests...
                Window = TimeSpan.FromMinutes(15),    // ...per rolling 15 minutes...
                SegmentsPerWindow = 3,                // ...checked in 5-minute slices
                QueueLimit = 0                         // no waiting room: excess -> immediate 429
            });
    });
});

// Read the Jwt settings once so we can reuse them below
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;

// Authentication — teach the app how to validate incoming JWTs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // CLAIM NAMING CONVENTION (read before adding controllers that use claims):
        // MapInboundClaims = false turns OFF the legacy WS-Fed remapping, so JWT claims
        // keep their SHORT names exactly as issued. The long ClaimTypes.* URIs are NOT
        // populated, so reading them returns null. In controllers, use short names:
        //   user id -> User.FindFirst("sub")    (NOT ClaimTypes.NameIdentifier)
        //   email   -> User.FindFirst("email")  (NOT ClaimTypes.Email)
        //   role    -> [Authorize(Roles="Admin")] / User.FindFirst("role")
        // RoleClaimType/NameClaimType below must match the short names the token uses.
        options.MapInboundClaims = false;   // keep short claim names ("role","sub") as-is
        options.TokenValidationParameters = new TokenValidationParameters
        {
            RoleClaimType = "role",                          // [Authorize(Roles=...)] reads the "role" claim
            NameClaimType = JwtRegisteredClaimNames.Sub,     // User.Identity.Name resolves to the user id ("sub")
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret))
        };
    });

// Redis Configuration
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnectionString ?? "localhost:6379"));


var blobConnectionString = builder.Configuration.GetConnectionString("AzureBlobStorage");
builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));

builder.Services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

var app = builder.Build();

// Seed the database in Development only
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();      // apply any pending migrations
    DbInitializer.Seed(db);     // insert seed data (runs once)
}

// 0. Honor X-Forwarded-* from the hosting proxy/load balancer so Request.IsHttps and Request.Scheme
// reflect the ORIGINAL client scheme (TLS is usually terminated at the proxy in prod). Without this,
// Request.IsHttps is false behind a proxy and the refresh-token cookie would be written insecure/Lax,
// breaking cross-site session restore. Gated to non-dev so it is a strict no-op locally.
if (!app.Environment.IsDevelopment())
{
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
    };
    // Cloud proxies use dynamic IPs and strip client-supplied forwarded headers, so trust them.
    forwardedOptions.KnownNetworks.Clear();
    forwardedOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedOptions);
}

// 1. Core Exception Interceptor (Handles errors before lower-level middlewares log them)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 2. Using Serilog for request logging:
app.UseSerilogRequestLogging(options =>
{
    // Tell Serilog how to treat different status codes (400-499 should be Warnings, not Errors)
    options.GetLevel = (httpContext, elapsedMs, authException) => 
    {
        if (httpContext.Response.StatusCode >= 500 || authException != null)
        {
            if (authException is Buyit.Domain.Exceptions.UnauthorizedException || 
                authException is Buyit.Domain.Exceptions.ValidationException ||
                authException is Buyit.Domain.Exceptions.NotFoundException ||
                authException is Buyit.Domain.Exceptions.ConflictException ||
                authException is Buyit.Domain.Exceptions.ExternalServiceException)
            {
                return Serilog.Events.LogEventLevel.Warning; 
            }
            return Serilog.Events.LogEventLevel.Error;
        }
        return Serilog.Events.LogEventLevel.Information;
    };

    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var userId = httpContext.User?.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
            diagnosticContext.Set("UserId", userId);

        diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
    };
});


// HTTP pipeline environment configurations
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();       // serves the built-in OpenAPI JSON at /openapi/v1.json
    app.UseSwagger();       // serves Swashbuckle's OpenAPI JSON
    app.UseSwaggerUI();     // serves the interactive UI page at /swagger
    app.UseCors(DevCors);   // permissive localhost CORS for the Vite dev server
}
else
{
    app.UseCors(ProdCors);  // named origins from config; needed for the SPA's credentialed XHRs
}

// Redirect HTTP->HTTPS only OUTSIDE development. In dev a 307 redirect on a credentialed,
// cross-origin XHR (login / refresh-token) drops the CORS headers and the browser aborts it,
// which broke the refresh flow when the frontend talked to the http profile (localhost:5000).
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// 3. Routing & Endpoint Protections (Must remain in this structural order)
app.UseRouting();
app.UseAuthentication();    // identify who the user is (reads & validates the token)
app.UseAuthorization();     // authorization checks based on claims/roles
app.UseRateLimiter();       // enforce per-endpoint rate limits (e.g. the "chat" policy)

app.MapControllers();       // route requests to your controllers

// Wrap app.Run() to catch fatal startup exceptions and flush all logs before exit
try
{
    app.Run();              // start listening for requests
}
catch (Exception ex)
{
    Serilog.Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Serilog.Log.CloseAndFlush();    // ensures all buffered logs are sent to Seq before exit
}