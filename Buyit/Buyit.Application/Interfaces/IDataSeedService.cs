using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

/// <summary>
/// Generates synthetic demo data (customers, multi-store orders, paid payments and
/// reviews) purely to populate the admin/seller dashboards. Dev-only; never wired
/// into any normal flow. Additive — it only inserts new rows and never mutates
/// existing products, stores or inventory.
/// </summary>
public interface IDataSeedService
{
    Task<SeedDataResponse> SeedDemoDataAsync(SeedDataRequest request);
}
