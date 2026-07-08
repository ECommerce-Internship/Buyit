namespace Buyit.Application.Common;

public class AzureQueueSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string LowStockQueueName { get; set; } = "low-stock-alerts";
    public string OrderConfirmationsQueueName { get; set; } = "order-confirmations";
    public int PollIntervalSeconds { get; set; } = 30;
}