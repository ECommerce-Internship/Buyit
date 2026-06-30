using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface IStoreService
{
    // Creates a Pending store owned by the given user. Returns the new store.
    Task<StoreResponse> CreateStoreForUserAsync(int ownerUserId, string name, string? description);

    // Admin: stores awaiting approval.
    Task<IReadOnlyList<StoreResponse>> GetPendingStoresAsync();

    // Admin: EVERY store (any status), ordered by name — for pickers like the product-create form.
    Task<IReadOnlyList<StoreResponse>> GetAllStoresAsync();

    // Admin moderation.
    Task<StoreResponse> ApproveAsync(int storeId);
    Task<StoreResponse> RejectAsync(int storeId);
    Task<StoreResponse> SuspendAsync(int storeId);

    // Public: a single APPROVED store by slug (404 otherwise).
    Task<StoreResponse> GetBySlugAsync(string slug);
}