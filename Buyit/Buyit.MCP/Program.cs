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

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddScoped<ICurrentUserService, McpCurrentUserService>();
builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILowStockAlertService, LowStockAlertService>();
builder.Services.AddScoped<IGeminiService, GeminiService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// MCP Server
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();
