using Buyit.Api.Extensions;
using Microsoft.EntityFrameworkCore;
using Buyit.Infrastructure.Data;
using Buyit.Application.Common;
using Buyit.Application.Interfaces;
using Buyit.Application.Validators;
using Buyit.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Buyit.Application.DTOs;
using Buyit.Api.Middleware;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

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
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
// JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<CreateCategoryRequest>, CreateCategoryRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateCategoryRequest>, UpdateCategoryRequestValidator>();

// Service Registration 
builder.Services.AddScoped<ICategoryService, CategoryService>();

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

var app = builder.Build();

// Seed the database in Development only
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();      // apply any pending migrations
    DbInitializer.Seed(db);     // insert seed data (runs once)
}


app.UseMiddleware<ExceptionHandlingMiddleware>();

// HTTP pipeline 
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();       // serves the built-in OpenAPI JSON at /openapi/v1.json
    app.UseSwagger();       // serves Swashbuckle's OpenAPI JSON
    app.UseSwaggerUI();     // serves the interactive UI page at /swagger
    app.UseCors(DevCors);   // apply the permissive CORS policy only in development
}

app.UseHttpsRedirection();  // redirect HTTP requests to HTTPS
app.UseAuthentication();//identify who the user is (reads & validates the token)
app.UseAuthorization();     // placeholder for when auth is added later
app.MapControllers();       // route requests to your controllers
app.Run();                  // start listening for requests


