using System.Reflection;
using Microsoft.OpenApi;

namespace Buyit.Api.Extensions;

/// <summary>
/// Registration helpers for configuring Swagger / OpenAPI generation,
/// including the API title, contact info, and JWT bearer security.
/// </summary>
public static class SwaggerServiceExtensions
{
    /// <summary>
    /// Configures SwaggerGen with the v1 document, JWT bearer auth, and XML comments.
    /// </summary>
    public static IServiceCollection AddBuyitSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            // (A) Describe version 1 of the API (title, description, contact)
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Buyit API",
                Version = "v1",
                Description = "E-commerce REST API built with .NET 10 and Clean Architecture.",
                Contact = new OpenApiContact
                {
                    Name = "Jason Rizk",
                    Email = "jason.rizk@example.com" // replace if you have the real address
                }
            });

            // (B) Define the Bearer JWT scheme -> this creates the "Authorize" button
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste only your JWT token here (no need to type 'Bearer ')."
            });

            // (C) Require that scheme (Microsoft.OpenApi v2 delegate form)
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
            });

            // (D) Feed the XML doc comments (generated in Step 2) into Swagger
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }
}