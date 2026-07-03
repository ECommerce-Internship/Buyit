using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Application.Validators;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using Buyit.MCP;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Load configuration from appsettings.json
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// EF Core — PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnectionString));

// Azure Blob Storage
var blobConnectionString = builder.Configuration.GetConnectionString("AzureBlobStorage") ?? "UseDevelopmentStorage=true";
builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
builder.Services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

// HTTP client (needed by GeminiService)
builder.Services.AddHttpClient("GeminiClient", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});


// Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<AzureQueueSettings>(builder.Configuration.GetSection("AzureQueue"));
builder.Services.Configure<SendGridSettings>(builder.Configuration.GetSection("SendGrid"));
builder.Services.Configure<EtherealSettings>(builder.Configuration.GetSection("Ethereal"));

// Validators
builder.Services.AddScoped<IValidator<CreateProductRequest>, CreateProductRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateProductRequest>, UpdateProductRequestValidator>();
builder.Services.AddScoped<IValidator<GenerateProductContentRequest>, GenerateProductContentRequestValidator>();
builder.Services.AddScoped<IValidator<GenerateContentRequest>, GenerateContentRequestValidator>();
builder.Services.AddScoped<IValidator<PlaceOrderRequest>, PlaceOrderRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateOrderStatusRequest>, UpdateOrderStatusRequestValidator>();
builder.Services.AddScoped<IValidator<ProcessPaymentRequest>, ProcessPaymentRequestValidator>();
builder.Services.AddScoped<IValidator<SubmitReviewRequest>, SubmitReviewRequestValidator>();
builder.Services.AddScoped<IValidator<CreateCategoryRequest>, CreateCategoryRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateCategoryRequest>, UpdateCategoryRequestValidator>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateProfileRequest>, UpdateProfileRequestValidator>();
builder.Services.AddScoped<IValidator<CreateStoreRequest>, CreateStoreRequestValidator>();
builder.Services.AddScoped<IValidator<RegisterSellerRequest>, RegisterSellerRequestValidator>();

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, McpCurrentUserService>();
builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILowStockAlertService, LowStockAlertService>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// MCP Server — HTTP (streamable) transport instead of stdio (TB-103).
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

// TB-103 security gate — authenticate the API -> MCP hop. The MCP server derives the caller's
// identity from the X-Buyit-Caller-* headers, so it MUST verify those headers actually came from
// the trusted API before trusting them. A shared secret does that. Without this gate, any client
// that can reach this HTTP endpoint could send X-Buyit-Caller-Role: Admin and run every tool.
// Fail closed: if the secret is unconfigured, or missing/wrong on a request, reject it (only the
// health probe is exempt, since Docker's health check can't carry the secret).
var mcpSharedSecret = builder.Configuration["Mcp:SharedSecret"];
var mcpSecretBytes = string.IsNullOrEmpty(mcpSharedSecret)
    ? Array.Empty<byte>()
    : Encoding.UTF8.GetBytes(mcpSharedSecret);

app.Use(async (context, next) =>
{
    // The liveness probe stays open — it returns no data and speaks no MCP.
    if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    // Never run wide open: a server with no secret configured refuses all tool traffic.
    if (mcpSecretBytes.Length == 0)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("MCP server is not configured (Mcp:SharedSecret missing).");
        return;
    }

    // Constant-time compare so the secret can't be recovered via a timing side-channel.
    var providedBytes = Encoding.UTF8.GetBytes(context.Request.Headers["X-Buyit-Mcp-Secret"].ToString());
    if (!CryptographicOperations.FixedTimeEquals(providedBytes, mcpSecretBytes))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized.");
        return;
    }

    await next();
});

// Simple liveness probe for Docker's health check (docker-compose). Returns 200 OK when the
// server is up. Kept separate from MapMcp so the health check never speaks the MCP protocol.
app.MapGet("/health", () => Results.Ok("healthy"));

// Expose the MCP protocol over HTTP at the root path. The API's HttpClientTransport
// (McpConnector) connects here. TB-103.
app.MapMcp();

await app.RunAsync();
