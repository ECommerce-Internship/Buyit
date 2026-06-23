namespace Buyit.Application.DTOs;

// One seller's slice of an order: its own status, money breakdown, and line items.
public record StoreOrderResponse(
    int StoreOrderId,
    int StoreId,
    string StoreName,
    string Status,
    decimal SubTotal,
    decimal CommissionAmount,
    decimal SellerNetAmount,
    IEnumerable<StoreOrderItemResponse> Items);
