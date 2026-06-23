using Buyit.Domain.Enums;

namespace Buyit.Domain.Entities;

/// <summary>
/// One seller's slice of a parent Order. A 3-store cart produces 1 Order and 3 StoreOrders.
/// Each StoreOrder is fulfilled and status-tracked independently by its seller.
/// </summary>
public class StoreOrder
{
    public int Id { get; set; }

    // The parent order this slice belongs to.
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    // The store that fulfils this slice.
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    // This store ships independently, so it has its own status.
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Money, captured at checkout. Precision (10,2) set in TB-121.
    public decimal SubTotal { get; set; }          // gross for this store
    public decimal CommissionAmount { get; set; }  // platform's cut
    public decimal SellerNetAmount { get; set; }   // SubTotal - CommissionAmount

    // The product lines in this slice.
    public ICollection<StoreOrderItem> StoreOrderItems { get; set; } = new List<StoreOrderItem>();
}