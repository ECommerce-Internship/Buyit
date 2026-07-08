namespace Buyit.Domain.Entities;

/// <summary>
/// Stock information for a single product (one-to-one with Product).
/// Kept separate so frequent stock changes don't churn the Product row.
/// </summary>
public class Inventory
{
    public int Id { get; set; }

    public int QuantityInStock { get; set; }

    // When stock drops to/below this value, a low-stock alert is raised.
    public int LowStockThreshold { get; set; } = 5;

    // Tracks when stock or threshold was last modified.
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    // Optimistic-concurrency token (H1). Configured as a rowversion in AppDbContext; Npgsql
    // maps a uint rowversion to the system "xmin" column, so two concurrent stock updates that
    // would oversell are detected (the loser throws DbUpdateConcurrencyException). No real
    // column is added.
    public uint Version { get; set; }

    // FK + a unique index (added in Step 3) guarantees the one-to-one link.
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}
