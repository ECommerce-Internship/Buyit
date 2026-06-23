namespace Buyit.Application.DTOs;

/// <summary>The safe public shape of a store.</summary>
public class StoreResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;   // "Pending" / "Approved" / "Suspended"
    public DateTime CreatedAt { get; set; }
}