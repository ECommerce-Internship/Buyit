using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

public interface IStoreService
{
    // Creates a Pending store owned by the given user. Returns the new store.
    Task<StoreResponse> CreateStoreForUserAsync(int ownerUserId, string name, string? description);

    // The signed-in seller's own stores, every status (Pending/Approved/Suspended/Rejected),
    // newest first. Used by GET /api/v1/stores/mine so the dashboard survives a page refresh.
    Task<IReadOnlyList<StoreResponse>> GetStoresForUserAsync(int ownerUserId);

    // Admin: stores awaiting approval.
    Task<IReadOnlyList<StoreResponse>> GetPendingStoresAsync();

    // Admin moderation.
    Task<StoreResponse> ApproveAsync(int storeId);
    Task<StoreResponse> RejectAsync(int storeId);
    Task<StoreResponse> SuspendAsync(int storeId);

    // Public: a single APPROVED store by slug (404 otherwise).
    Task<StoreResponse> GetBySlugAsync(string slug);
}