using Asp.Versioning;

namespace Buyit.Api.Extensions;

/// <summary>
/// Registration helpers for URL-segment API versioning and its Swagger integration.
/// </summary>
public static class ApiVersioningExtensions
{
    /// <summary>
    /// Enables API versioning (default v1.0) and exposes versions to the API Explorer
    /// so Swagger can group endpoints by version (e.g. "v1").
    /// </summary>
    public static IServiceCollection AddBuyitApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}