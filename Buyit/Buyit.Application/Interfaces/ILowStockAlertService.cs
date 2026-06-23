namespace Buyit.Application.Interfaces;

public interface ILowStockAlertService
{
    // Serializes the low stock message and sends it to the Azure Queue
    Task TriggerAlertAsync(int productId, string productName, int quantity, int threshold);
}