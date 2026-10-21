using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Contracts;
using Shared.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging("NotificationService");
var rabbitMqOptions = builder.Configuration.GetSection("RabbitMq").Get<NotificationRabbitMqOptions>() ?? new NotificationRabbitMqOptions();
builder.Services.AddSingleton(rabbitMqOptions);
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogistics Notification API",
        Version = "v1"
    });
});

var app = builder.Build();
app.UseApiInfrastructure();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartLogistics Notification API v1"));
}
app.MapGet("/health", () => Results.Ok(new { service = "NotificationService", status = "Ready" }));
app.Run();

public sealed class NotificationRabbitMqOptions
{
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string Exchange { get; init; } = "smartlogistics.events";
    public string QueueName { get; init; } = "notification-service";
}

public sealed class NotificationWorker(NotificationRabbitMqOptions options, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { HostName = options.HostName, Port = options.Port, UserName = options.UserName, Password = options.Password };
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(options.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        foreach (var eventName in new[] { EventNames.OrderCreated, EventNames.StockReserved, EventNames.OrderPacked, EventNames.OrderDispatched, EventNames.DeliveryAssigned, EventNames.OrderDelivered })
        {
            await channel.QueueBindAsync(options.QueueName, options.Exchange, eventName, cancellationToken: stoppingToken);
        }

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var message = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            logger.LogInformation("Notification event received: {Message}", message);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };
        await channel.BasicConsumeAsync(options.QueueName, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}