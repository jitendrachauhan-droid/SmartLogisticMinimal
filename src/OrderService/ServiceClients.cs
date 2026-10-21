using System.Net.Http.Json;

namespace OrderService;

public sealed class ServiceEndpointsOptions
{
    public string DeliveryServiceUrl { get; init; } = "http://localhost:5005";
}

public sealed record CreateDeliveryCommand(Guid OrderId, string DeliveryAddress);

public sealed class DeliveryServiceClient(HttpClient httpClient, ILogger<DeliveryServiceClient> logger)
{
    public async Task<bool> CreateAsync(OrderResponse order, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("/api/deliveries", new CreateDeliveryCommand(order.OrderId, order.DeliveryAddress), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            logger.LogWarning("Delivery Service returned {StatusCode} for order {OrderId}.", response.StatusCode, order.OrderId);
            return false;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Delivery Service was unavailable for order {OrderId}.", order.OrderId);
            return false;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Delivery Service timed out for order {OrderId}.", order.OrderId);
            return false;
        }
    }
}