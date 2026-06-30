using System.ComponentModel.DataAnnotations;

namespace Buyit.Domain.Entities;

/// <summary>
/// A product grouping. Categories can nest (subcategories) via the single
/// self-referencing ParentCategoryId.
/// </summary>
public class Category
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    // Self-referencing FK: null = top-level category; a value = subcategory.
    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
