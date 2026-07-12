namespace Buyit.Application.DTOs;

/// <summary>What a demo-data seeding run created, so the caller can see the effect.</summary>
public record SeedDataResponse(
    int CustomersCreated,
    int OrdersCreated,
    int StoreOrdersCreated,
    int LineItemsCreated,
    int PaymentsCreated,
    int ReviewsCreated);
