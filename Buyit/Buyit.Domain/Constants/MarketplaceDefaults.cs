namespace Buyit.Domain.Constants;

/// <summary>
/// Default values for the multi-vendor marketplace. Centralized so the platform's
/// commission rate has a single source of truth instead of being hand-typed per call site.
/// </summary>
public static class MarketplaceDefaults
{
    /// <summary>Platform cut applied to a new seller store, as a fraction (0.15 = 15%).</summary>
    public const decimal CommissionRate = 0.15m;
}
