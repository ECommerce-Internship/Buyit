namespace Buyit.Application.DTOs;

/// <summary>
/// Knobs for the dev-only demo-data seeder. All fields are optional; the seeder
/// clamps them to sane bounds. <paramref name="Seed"/> makes a run reproducible.
/// </summary>
public record SeedDataRequest(
    int Customers = 20,
    int Orders = 200,
    int DaysBack = 365,
    int? Seed = null);
