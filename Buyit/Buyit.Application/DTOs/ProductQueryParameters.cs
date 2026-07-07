namespace Buyit.Application.DTOs;

/// <summary>
/// All the optional knobs for GET /products: searching, filtering, sorting, paging.
/// ASP.NET binds the URL query string (?search=...&page=...) onto these properties.
/// </summary>
public class ProductQueryParameters
{
    // ---------- FILTERING ----------
    public string? Search { get; set; }       // matches against product Name (Contains)
    public int? CategoryId { get; set; }       // null = any category
    public decimal? MinPrice { get; set; }     // null = no lower bound
    public decimal? MaxPrice { get; set; }     // null = no upper bound
    public int? StoreId { get; set; }          // when set: scope to ONE store (seller/admin management view)


    // ---------- SORTING ----------
    // Allowed values: "name", "price", "createdAt". Anything else falls back to a default.
    public string? SortBy { get; set; }
    // true = descending (Z->A, high->low, newest first). Default false = ascending.
    public bool SortDescending { get; set; } = false;

    // ---------- PAGING ----------
    private const int MaxPageSize = 50;   // hard ceiling so nobody can request 1,000,000
    private int _pageSize = 10;           // backing field for the PageSize property

    public int Page { get; set; } = 1;    // 1-based page number; default first page

    public int PageSize
    {
        get => _pageSize;
        // Clamp: if a caller asks for more than MaxPageSize, quietly cap it.
        set => _pageSize = (value > MaxPageSize) ? MaxPageSize : (value < 1 ? 10 : value);
    }
}