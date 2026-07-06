namespace Buyit.Application.DTOs;

/// <summary>The safe public shape of a store.</summary>
public record StoreResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;   // "Pending" / "Approved" / "Suspended"
    public DateTime CreatedAt { get; set; }
    // The owner's display name ("First Last"). Null when the Owner navigation wasn't loaded
    // (only the admin store lists Include it). Added for TB-140's owner column.
    public string? OwnerName { get; set; }
}