using InventoryService;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Microsoft.OpenApi;
var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging("InventoryService");
var connectionString = builder.Configuration.GetConnectionString("InventoryDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:InventoryDb must be configured.");
}

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
builder.Services.AddScoped<InventoryStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogistics Inventory API",
        Version = "v1",
        Description = "Inventory management API."
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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartLogistics Inventory API v1"));
}

app.MapPost("/api/inventory/products", async (CreateProductRequest request, InventoryStore store, CancellationToken cancellationToken) =>
{
    var errors = InventoryValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var product = await store.AddProductAsync(request, cancellationToken);
    return Results.Created($"/api/inventory/{product.ProductId}", product);
})
.WithName("AddProduct")
.WithSummary("Add a product and initial stock")
.Produces<ProductResponse>(StatusCodes.Status201Created)
.ProducesValidationProblem();

app.MapPut("/api/inventory/products/{productId:guid}", async (Guid productId, UpdateProductRequest request, InventoryStore store, CancellationToken cancellationToken) =>
{
    var product = await store.UpdateProductAsync(productId, request, cancellationToken);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.WithName("UpdateProduct")
.WithSummary("Update product details and low-stock threshold")
.Produces<ProductResponse>()
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/inventory/{productId:guid}", async (Guid productId, InventoryStore store, CancellationToken cancellationToken) =>
{
    var product = await store.GetAsync(productId, cancellationToken);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.WithName("GetInventory")
.WithSummary("Get stock availability for a product")
.Produces<ProductResponse>()
.Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/inventory/reserve", async (StockOperationRequest request, InventoryStore store, CancellationToken cancellationToken) =>
{
    var errors = InventoryValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var product = await store.ReserveAsync(request.ProductId, request.Quantity, cancellationToken);
    return product is null
        ? Results.Conflict(new { message = "Product does not exist or does not have enough available stock." })
        : Results.Ok(product);
})
.WithName("ReserveStock")
.WithSummary("Reserve available stock without overselling")
.Produces<ProductResponse>()
.Produces(StatusCodes.Status409Conflict)
.ProducesValidationProblem();

app.MapPost("/api/inventory/release", async (StockOperationRequest request, InventoryStore store, CancellationToken cancellationToken) =>
{
    var errors = InventoryValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var product = await store.ReleaseAsync(request.ProductId, request.Quantity, cancellationToken);
    return product is null
        ? Results.Conflict(new { message = "Product does not exist or does not have enough reserved stock." })
        : Results.Ok(product);
})
.WithName("ReleaseStock")
.WithSummary("Release reserved stock")
.Produces<ProductResponse>()
.Produces(StatusCodes.Status409Conflict)
.ProducesValidationProblem();

app.MapPost("/api/inventory/deduct", async (StockOperationRequest request, InventoryStore store, CancellationToken cancellationToken) =>
{
    var errors = InventoryValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var product = await store.DeductAsync(request.ProductId, request.Quantity, cancellationToken);
    return product is null
        ? Results.Conflict(new { message = "Product does not exist or does not have enough reserved stock to deduct." })
        : Results.Ok(product);
})
.WithName("DeductStock")
.WithSummary("Deduct reserved stock after fulfillment")
.Produces<ProductResponse>()
.Produces(StatusCodes.Status409Conflict)
.ProducesValidationProblem();

app.Run();

public partial class Program;