namespace Buyit.Application.DTOs;

/// <summary>The data a client submits to update an existing product (PUT body).</summary>
public class UpdateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public int CategoryId { get; set; }
    public string? SeoTitle { get; set; }
    public string? MetaDescription { get; set; }
}