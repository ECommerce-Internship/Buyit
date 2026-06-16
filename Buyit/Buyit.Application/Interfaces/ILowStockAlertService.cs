namespace Buyit.Application.Interfaces;

public interface ILowStockAlertService
{
    // Full implementation wired in the Azure epic
    Task TriggerAlertAsync(int productId, string productName, int quantity);
}