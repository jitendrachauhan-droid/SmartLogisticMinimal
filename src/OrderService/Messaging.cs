using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Shared.Contracts;

namespace OrderService;

public sealed class RabbitMqOptions
{
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string Exchange { get; init; } = "smartlogistics.events";
}

public interface IEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

public sealed class RabbitMqEventPublisher(RabbitMqOptions options, ILogger<RabbitMqEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = options.HostName,
                Port = options.Port,
                UserName = options.UserName,
                Password = options.Password
            };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(integrationEvent));
            await channel.BasicPublishAsync(options.Exchange, integrationEvent.EventName, body, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not publish integration event {EventName} with ID {EventId}.", integrationEvent.EventName, integrationEvent.EventId);
            throw;
        }
    }
}