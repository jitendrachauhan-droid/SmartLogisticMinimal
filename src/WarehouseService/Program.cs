using Microsoft.EntityFrameworkCore;
using WarehouseService;
using Shared.Infrastructure;
using Microsoft.OpenApi;
var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging("WarehouseService");
var connectionString = builder.Configuration.GetConnectionString("WarehouseDb");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:WarehouseDb must be configured.");
}

builder.Services.AddDbContext<WarehouseDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
builder.Services.AddScoped<WarehouseStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogistics Warehouse API",
        Version = "v1",
        Description = "Warehouse management API."
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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartLogistics Warehouse API v1"));
}

app.MapPost("/api/warehouses", async (CreateWarehouseRequest request, WarehouseStore store, CancellationToken cancellationToken) =>
{
    var error = WarehouseValidation.Validate(request);
    if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { [error.Field] = [error.Message] });
    var warehouse = await store.CreateAsync(request, cancellationToken);
    return Results.Created($"/api/warehouses/{warehouse.WarehouseId}", warehouse);
})
.WithName("CreateWarehouse").WithSummary("Create a warehouse").Produces<WarehouseResponse>(StatusCodes.Status201Created).ProducesValidationProblem();

app.MapGet("/api/warehouses/{id:guid}", async (Guid id, WarehouseStore store, CancellationToken cancellationToken) =>
{
    var warehouse = await store.GetAsync(id, cancellationToken);
    return warehouse is null ? Results.NotFound() : Results.Ok(warehouse);
})
.WithName("GetWarehouse").WithSummary("Get warehouse availability and capacity").Produces<WarehouseResponse>().Produces(StatusCodes.Status404NotFound);

app.MapPut("/api/warehouses/{id:guid}", async (Guid id, UpdateWarehouseRequest request, WarehouseStore store, CancellationToken cancellationToken) =>
{
    var warehouse = await store.UpdateAsync(id, request, cancellationToken);
    return warehouse is null ? Results.Conflict(new { message = "Warehouse does not exist or capacity is below current usage." }) : Results.Ok(warehouse);
})
.WithName("UpdateWarehouse").WithSummary("Update warehouse details and capacity").Produces<WarehouseResponse>().Produces(StatusCodes.Status409Conflict);

app.MapPost("/api/warehouses/{id:guid}/inventory", async (Guid id, MapInventoryRequest request, WarehouseStore store, CancellationToken cancellationToken) =>
{
    var warehouse = await store.MapInventoryAsync(id, request, cancellationToken);
    return warehouse is null ? Results.Conflict(new { message = "Warehouse does not exist, product mapping is invalid, or capacity is insufficient." }) : Results.Ok(warehouse);
})
.WithName("MapWarehouseInventory").WithSummary("Map product inventory to a warehouse").Produces<WarehouseResponse>().Produces(StatusCodes.Status409Conflict);

app.MapPost("/api/warehouses/select", async (SelectWarehouseRequest request, WarehouseStore store, CancellationToken cancellationToken) =>
{
    var warehouse = await store.SelectAsync(request, cancellationToken);
    return warehouse is null ? Results.NotFound(new { message = "No available warehouse can fulfil the requested products and capacity." }) : Results.Ok(warehouse);
})
.WithName("SelectWarehouse").WithSummary("Select the best warehouse for an order").Produces<WarehouseSelectionResponse>().Produces(StatusCodes.Status404NotFound);

app.Run();

public partial class Program;