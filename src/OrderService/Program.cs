using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using OrderService;
using Shared.Contracts;
using Shared.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging("OrderService");
var connectionString = builder.Configuration.GetConnectionString("OrderDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:OrderDb must be configured.");
}

builder.Services.AddDbContext<OrderDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<OrderStore>();
builder.Services.AddScoped<IOrderProcessor, OrderProcessor>();
var serviceEndpoints = builder.Configuration.GetSection("ServiceEndpoints").Get<ServiceEndpointsOptions>() ?? new ServiceEndpointsOptions();
builder.Services.AddHttpClient<DeliveryServiceClient>(client =>
{
    client.BaseAddress = new Uri(serviceEndpoints.DeliveryServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(20);
})
.AddStandardResilienceHandler(options =>
{
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    options.Retry.MaxRetryAttempts = 2;
    options.Retry.Delay = TimeSpan.FromMilliseconds(250);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
});
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value);
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogistics Order API",
        Version = "v1"
    });

    const string bearerScheme = "Bearer";

    options.AddSecurityDefinition(bearerScheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT access token."
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(bearerScheme, document)] =
                new List<string>()
        });
});

var app = builder.Build();
app.UseApiInfrastructure();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartLogistics Order API v1"));
}

app.MapPost("/api/orders", async (CreateOrderRequest request, OrderStore store, IEventPublisher eventPublisher, CancellationToken cancellationToken) =>
{
    var errors = OrderValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var order = await store.CreateAsync(request, cancellationToken);
    await eventPublisher.PublishAsync(new OrderCreatedEvent(
        order.OrderId,
        order.CustomerId,
        order.WarehouseId,
        order.TotalAmount,
        order.DeliveryAddress,
        order.OrderDate).ToIntegrationEvent(), cancellationToken);
    return Results.Created($"/api/orders/{order.OrderId}", order);
})
.WithName("CreateOrder")
.WithSummary("Create an order")
.Produces<OrderResponse>(StatusCodes.Status201Created)
.ProducesValidationProblem();

app.MapGet("/api/orders/{orderId:guid}", async (Guid orderId, OrderStore store, CancellationToken cancellationToken) =>
{
    var order = await store.GetAsync(orderId, cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
})
.WithName("GetOrder")
.WithSummary("Get an order")
.Produces<OrderResponse>()
.Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/orders/{orderId:guid}/process", async (Guid orderId, IOrderProcessor processor, CancellationToken cancellationToken) =>
{
    var result = await processor.ProcessOrderAsync(orderId, cancellationToken);
    return result.Succeeded
        ? Results.Ok(result.Value)
        : Results.Conflict(new { errorCode = result.Error, message = "The order cannot be processed in its current state." });
})
.WithName("ProcessOrder")
.WithSummary("Advance an order by one lifecycle state")
.Produces<OrderResponse>()
.Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/orders/{orderId:guid}/status", async (Guid orderId, UpdateOrderStatusRequest request, OrderStore store, DeliveryServiceClient deliveryClient, IEventPublisher eventPublisher, CancellationToken cancellationToken) =>
{
    var result = await store.UpdateStatusAsync(orderId, request.Status, cancellationToken);
    if (result.Order is null)
    {
        return Results.NotFound();
    }

    return result.InvalidTransition
        ? Results.Conflict(new { message = $"Cannot transition order from {result.Order.OrderStatus} to {request.Status}." })
        : await PublishStatusAndReturnAsync(result.Order, request.Status, deliveryClient, eventPublisher, cancellationToken);
})
.WithName("UpdateOrderStatus")
.WithSummary("Advance an order through its lifecycle")
.Produces<OrderResponse>()
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict);

app.MapPut("/api/orders/{orderId:guid}/payment-status", async (Guid orderId, UpdatePaymentStatusRequest request, OrderStore store, CancellationToken cancellationToken) =>
{
    var order = await store.UpdatePaymentStatusAsync(orderId, request.Status, cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
})
.WithName("UpdateOrderPaymentStatus")
.WithSummary("Update order payment status")
.Produces<OrderResponse>()
.Produces(StatusCodes.Status404NotFound);

app.Run();

static async Task<IResult> PublishStatusAndReturnAsync(OrderResponse order, OrderStatus status, DeliveryServiceClient deliveryClient, IEventPublisher eventPublisher, CancellationToken cancellationToken)
{
    var integrationEvent = status switch
    {
        OrderStatus.Packed => new IntegrationEvent(Guid.NewGuid().ToString(), EventNames.OrderPacked, DateTimeOffset.UtcNow, System.Text.Json.JsonSerializer.Serialize(order)),
        OrderStatus.Dispatched => new IntegrationEvent(Guid.NewGuid().ToString(), EventNames.OrderDispatched, DateTimeOffset.UtcNow, System.Text.Json.JsonSerializer.Serialize(order)),
        OrderStatus.Delivered => new IntegrationEvent(Guid.NewGuid().ToString(), EventNames.OrderDelivered, DateTimeOffset.UtcNow, System.Text.Json.JsonSerializer.Serialize(order)),
        _ => null
    };

    if (status == OrderStatus.Dispatched)
    {
        var deliveryCreated = await deliveryClient.CreateAsync(order, cancellationToken);
        if (!deliveryCreated)
        {
            return Results.Json(new ApiErrorResponse(false, "DELIVERY_SERVICE_UNAVAILABLE", "Delivery Service is temporarily unavailable. Please retry the dispatch operation."), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    if (integrationEvent is not null)
    {
        await eventPublisher.PublishAsync(integrationEvent, cancellationToken);
    }

    return Results.Ok(order);
}

public partial class Program;