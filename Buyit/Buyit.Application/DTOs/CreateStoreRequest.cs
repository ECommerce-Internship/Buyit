namespace Buyit.Application.DTOs;

/// <summary>Payload for an existing seller to open an additional store.</summary>
public class CreateStoreRequest
{
    public string StoreName { get; set; } = string.Empty;
    public string? StoreDescription { get; set; }
}