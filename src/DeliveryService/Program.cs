using DeliveryService;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging("DeliveryService");
var connectionString = builder.Configuration.GetConnectionString("DeliveryDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DeliveryDb must be configured.");
}

builder.Services.AddDbContext<DeliveryDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<DeliveryStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogistics Delivery API",
        Version = "v1",
        Description = "Delivery management API."
    });
    // Add JWT Bearer authentication
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token here. Example: eyJhbGciOiJIUzI1NiIs..."
    });
    // Require Bearer token for API endpoints in Swagger
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});
var app = builder.Build();
app.UseApiInfrastructure();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartLogistics Delivery API v1"));
}

app.MapPost("/api/deliveries", async (CreateDeliveryRequest request, DeliveryStore store, CancellationToken cancellationToken) =>
{
    if (request.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeliveryAddress) || request.DeliveryAddress.Trim().Length > 500)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["delivery"] = ["OrderId and DeliveryAddress are required, and DeliveryAddress cannot exceed 500 characters."]
        });
    }

    var delivery = await store.CreateAsync(request, cancellationToken);
    return Results.Created($"/api/deliveries/{delivery.Id}", delivery);
})
.WithName("CreateDelivery")
.WithSummary("Create a delivery")
.Produces<DeliveryResponse>(StatusCodes.Status201Created)
.ProducesValidationProblem();

app.MapGet("/api/deliveries/{id:guid}", async (Guid id, DeliveryStore store, CancellationToken cancellationToken) =>
{
    var delivery = await store.GetAsync(id, cancellationToken);
    return delivery is null ? Results.NotFound() : Results.Ok(delivery);
})
.WithName("GetDelivery")
.WithSummary("Get a delivery")
.Produces<DeliveryResponse>()
.Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/deliveries/{id:guid}/assign", async (Guid id, AssignDeliveryAgentRequest request, DeliveryStore store, CancellationToken cancellationToken) =>
{
    if (request.AgentId == Guid.Empty)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["agentId"] = ["AgentId is required."]
        });
    }

    var delivery = await store.AssignAsync(id, request.AgentId, cancellationToken);
    return delivery is null
        ? Results.Conflict(new { message = "Delivery does not exist or is not in Created status." })
        : Results.Ok(delivery);
})
.WithName("AssignDeliveryAgent")
.WithSummary("Assign a delivery agent")
.Produces<DeliveryResponse>()
.Produces(StatusCodes.Status409Conflict)
.ProducesValidationProblem();

app.MapPut("/api/deliveries/{id:guid}/status", async (Guid id, UpdateDeliveryStatusRequest request, DeliveryStore store, CancellationToken cancellationToken) =>
{
    var result = await store.UpdateStatusAsync(id, request.Status, cancellationToken);
    if (result.Delivery is null)
    {
        return Results.NotFound();
    }

    return result.InvalidTransition
        ? Results.Conflict(new { message = $"Cannot transition delivery from {result.Delivery.Status} to {request.Status}." })
        : Results.Ok(result.Delivery);
})
.WithName("UpdateDeliveryStatus")
.WithSummary("Update delivery status")
.Produces<DeliveryResponse>()
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict);

app.MapGet("/api/deliveries/{id:guid}/tracking", async (Guid id, DeliveryStore store, CancellationToken cancellationToken) =>
{
    var tracking = await store.GetTrackingAsync(id, cancellationToken);
    return tracking is null ? Results.NotFound() : Results.Ok(tracking);
})
.WithName("GetDeliveryTracking")
.WithSummary("Get delivery tracking history")
.Produces<DeliveryTrackingResponse>()
.Produces(StatusCodes.Status404NotFound);

app.Run();

public partial class Program;