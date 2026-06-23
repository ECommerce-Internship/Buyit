using Buyit.Api.Extensions;
using Buyit.Api.Middleware;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Application.Validators;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using Serilog;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Azure.Storage.Blobs;

// Bootstrap logger — captures startup errors before the host config is loaded
Log.Logger = new LoggerConfiguration()
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
        .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341");
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
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCors, policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// EF Core — register the database context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
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
builder.Services.AddScoped<IValidator<PlaceOrderRequest>, PlaceOrderRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateOrderStatusRequest>, UpdateOrderStatusRequestValidator>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IOrderService, OrderService>();
// --- TB-40: Payment feature registrations ---
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IValidator<ProcessPaymentRequest>, ProcessPaymentRequestValidator>();
builder.Services.AddScoped<IEmailService, EmailService>();
// --- TB-32: Product feature registrations ---
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IValidator<CreateProductRequest>, CreateProductRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateProductRequest>, UpdateProductRequestValidator>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ILowStockAlertService, LowStockAlertService>();
builder.Services.AddScoped<ICacheService, CacheService>();
// --- TB-46: Gemini AI product-content feature registrations ---
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));

builder.Services.AddHttpClient("GeminiClient", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IValidator<GenerateProductContentRequest>, GenerateProductContentRequestValidator>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

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
    app.UseCors(DevCors);   // apply the permissive CORS policy only in development
}

app.UseHttpsRedirection();  // redirect HTTP requests to HTTPS

// 3. Routing & Endpoint Protections (Must remain in this structural order)
app.UseRouting();
app.UseAuthentication();    // identify who the user is (reads & validates the token)
app.UseAuthorization();     // authorization checks based on claims/roles

app.MapControllers();       // route requests to your controllers

// Wrap app.Run() to catch fatal startup exceptions and flush all logs before exit
try
{
    app.Run();              // start listening for requests
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();    // ensures all buffered logs are sent to Seq before exit
}