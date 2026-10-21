using CustomerService;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.AddStructuredLogging("CustomerService");

builder.Services.AddDbContext<CustomerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CustomerDb")));
builder.Services.AddScoped<ICustomerStore, EfCustomerStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogistics Customer API",
        Version = "v1",
        Description = "Customer management API."
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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartLogistics Customer API v1"));
}

app.MapPost("/api/customers", async (CreateCustomerRequest request, ICustomerStore store, CancellationToken cancellationToken) =>
{
    var validationError = CustomerValidation.Validate(request.Name, request.Email);
    if (validationError is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [validationError.Field] = [validationError.Message]
        });
    }

    var customer = new Customer(Guid.NewGuid(), request.Name!.Trim(), request.Email!.Trim(), CustomerStatus.Active, DateTimeOffset.UtcNow);
    await store.AddAsync(customer, cancellationToken);
    return Results.Created($"/api/customers/{customer.Id}", customer);
});

app.MapGet("/api/customers/{id:guid}", async (Guid id, ICustomerStore store, CancellationToken cancellationToken) =>
{
    var customer = await store.GetAsync(id, cancellationToken);
    return customer is null ? Results.NotFound() : Results.Ok(customer);
});

app.MapGet("/api/customers", async (int? page, int? pageSize, CustomerStatus? status, string? sortBy, ICustomerStore store, CancellationToken cancellationToken) =>
{
    var queryErrors = CustomerValidation.ValidateQuery(page, pageSize, sortBy);
    if (queryErrors.Count > 0)
    {
        return Results.ValidationProblem(queryErrors);
    }

    var result = await store.SearchAsync(new CustomerQuery(
        page ?? CustomerValidation.DefaultPage,
        pageSize ?? CustomerValidation.DefaultPageSize,
        status,
        sortBy), cancellationToken);
    return Results.Ok(result);
});

app.MapPut("/api/customers/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICustomerStore store, CancellationToken cancellationToken) =>
{
    var validationError = CustomerValidation.Validate(request.Name, request.Email);
    if (validationError is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [validationError.Field] = [validationError.Message]
        });
    }

    var customer = await store.UpdateAsync(id, request.Name!.Trim(), request.Email!.Trim(), cancellationToken);
    return customer is null ? Results.NotFound() : Results.Ok(customer);
});

app.MapDelete("/api/customers/{id:guid}", async (Guid id, ICustomerStore store, CancellationToken cancellationToken) =>
{
    var deactivated = await store.DeactivateAsync(id, cancellationToken);
    return deactivated ? Results.NoContent() : Results.NotFound();
});

app.Run();

public partial class Program;