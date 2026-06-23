namespace Buyit.Application.DTOs;

public record TopProductResponse(
    int ProductId,
    string ProductName,
    int UnitsSold,
    decimal Revenue);