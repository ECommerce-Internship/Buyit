namespace Buyit.Application.DTOs;

/// <summary>
/// A single page of results plus the metadata a client needs to build paging controls.
/// Generic so ANY list endpoint can reuse it: PaginatedResult&lt;ProductResponse&gt;, etc.
/// </summary>
/// <typeparam name="T">The type of the items on the page.</typeparam>
public class PaginatedResult<T>
{
    // The items on THIS page only (e.g. 10 products), never the whole table.
    public List<T> Items { get; set; } = new();

    public int Page { get; set; }          // which page this is (1-based)
    public int PageSize { get; set; }      // how many items per page were requested
    public int TotalCount { get; set; }    // how many items exist in total (all pages)
    public int TotalPages { get; set; }    // ceil(TotalCount / PageSize)

    // Convenience flags so the front-end can enable/disable Prev/Next buttons.
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}