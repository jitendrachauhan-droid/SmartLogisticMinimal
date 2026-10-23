using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts;

namespace InventoryService;

public sealed class InventoryRabbitMqOptions
{
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string Exchange { get; init; } = "smartlogistics.events";
    public string QueueName { get; init; } = "inventory-service";
}

public sealed class InventoryServiceEndpointsOptions
{
    public string WarehouseServiceUrl { get; init; } = "http://localhost:5250";
}

public sealed class WarehouseServiceClient(HttpClient httpClient)
{
    public async Task<bool> IsAvailableAsync(Guid warehouseId, int requiredCapacity, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"/api/warehouses/{warehouseId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var warehouse = await response.Content.ReadFromJsonAsync<WarehouseAvailabilityResponse>(cancellationToken);
        return warehouse is { IsAvailable: true } && warehouse.AvailableCapacity >= requiredCapacity;
    }

    private sealed record WarehouseAvailabilityResponse(bool IsAvailable, int AvailableCapacity);
}

public sealed class OrderCreatedWorker(
    InventoryRabbitMqOptions options,
    WarehouseServiceClient warehouseClient,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderCreatedWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password
        };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(options.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(options.QueueName, options.Exchange, EventNames.OrderCreated, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<IntegrationEvent>(eventArgs.Body.Span);
                var order = envelope is { EventName: EventNames.OrderCreated }
                    ? JsonSerializer.Deserialize<OrderCreatedEvent>(envelope.Payload)
                    : null;
                if (order is null || order.OrderId == Guid.Empty || order.WarehouseId == Guid.Empty || order.Items is not { Count: > 0 })
                {
                    logger.LogError("Discarding invalid OrderCreated message with delivery tag {DeliveryTag}.", eventArgs.DeliveryTag);
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    return;
                }

                if (order.Items.Any(item => item.ProductId == Guid.Empty || item.Quantity <= 0))
                {
                    logger.LogError("Discarding OrderCreated message for order {OrderId} with invalid items.", order.OrderId);
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    return;
                }

                var requiredCapacity = order.Items.Sum(item => item.Quantity);
                if (!await warehouseClient.IsAvailableAsync(order.WarehouseId, requiredCapacity, stoppingToken))
                {
                    logger.LogWarning("Order {OrderId} references an unavailable warehouse or insufficient capacity.", order.OrderId);
                    await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    return;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var inventoryStore = scope.ServiceProvider.GetRequiredService<InventoryStore>();
                var reservedItems = new List<OrderCreatedItem>();
                foreach (var item in order.Items)
                {
                    var reserved = await inventoryStore.ReserveAsync(item.ProductId, item.Quantity, stoppingToken);
                    if (reserved is null)
                    {
                        foreach (var reservedItem in reservedItems)
                        {
                            await inventoryStore.ReleaseAsync(reservedItem.ProductId, reservedItem.Quantity, stoppingToken);
                        }

                        logger.LogWarning("Insufficient inventory for order {OrderId}, product {ProductId}; prior reservations were released.", order.OrderId, item.ProductId);
                        await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                        return;
                    }

                    reservedItems.Add(item);
                }

                foreach (var item in reservedItems)
                {
                    var stockReserved = new StockReservedEvent(order.OrderId, item.ProductId, item.Quantity, DateTimeOffset.UtcNow);
                    var stockEnvelope = new IntegrationEvent(Guid.NewGuid().ToString(), EventNames.StockReserved, stockReserved.OccurredAt, JsonSerializer.Serialize(stockReserved));
                    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(stockEnvelope));
                    await channel.BasicPublishAsync(options.Exchange, EventNames.StockReserved, body, stoppingToken);
                }

                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                logger.LogInformation("Reserved inventory for order {OrderId} at warehouse {WarehouseId}.", order.OrderId, order.WarehouseId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to process OrderCreated message; it will be requeued.");
                await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(options.QueueName, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}