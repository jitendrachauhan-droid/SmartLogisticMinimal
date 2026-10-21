//using System.Diagnostics;
//using System.Text;
//using System.Threading.RateLimiting;
//using Microsoft.AspNetCore.Authentication.JwtBearer;
//using Microsoft.AspNetCore.RateLimiting;
//using Microsoft.IdentityModel.Tokens;
//using Shared.Infrastructure;
//using Yarp.ReverseProxy;
//using Scalar.AspNetCore;

//var builder = WebApplication.CreateBuilder(args);
//builder.AddStructuredLogging("ApiGateway");
//var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
//if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
//{
//    throw new InvalidOperationException("Jwt:SigningKey must be supplied through secure configuration and contain at least 32 bytes.");
//}

//builder.Services.AddOpenApi(options =>
//{
//    options.AddDocumentTransformer((document, context, cancellationToken) =>
//    {
//        document.Info.Title = "SmartLogistics API Gateway";
//        document.Info.Version = "v1";

//        document.Components ??= new Microsoft.OpenApi.Models.OpenApiComponents();

//        document.Components.SecuritySchemes["Bearer"] =
//            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
//            {
//                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
//                Scheme = "bearer",
//                BearerFormat = "JWT",
//                Description = "Enter your JWT token."
//            };

//        return Task.CompletedTask;
//    });
//});
//builder.Services.AddSingleton(jwt);
//builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//    .AddJwtBearer(options =>
//    {
//        options.TokenValidationParameters = new TokenValidationParameters
//        {
//            ValidateIssuer = true,
//            ValidateAudience = true,
//            ValidateLifetime = true,
//            ValidateIssuerSigningKey = true,
//            ValidIssuer = jwt.Issuer,
//            ValidAudience = jwt.Audience,
//            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
//            ClockSkew = TimeSpan.FromSeconds(30)
//        };
//    });
//builder.Services.AddAuthorizationBuilder()
//    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
//builder.Services.AddRateLimiter(options =>
//{
//    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
//    options.AddPolicy("gateway", context => RateLimitPartition.GetFixedWindowLimiter(
//        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
//        _ => new FixedWindowRateLimiterOptions
//        {
//            PermitLimit = 100,
//            Window = TimeSpan.FromMinutes(1),
//            QueueLimit = 0
//        }));
//});
//builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

//var app = builder.Build();
//app.UseApiInfrastructure();

//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartLogistics API Gateway v1"));

//    app.MapScalarApiReference("/swagger", options =>
//    {
//        options.WithTitle("SmartLogistics API Gateway");
//        options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
//    });
//}

//app.Use(async (context, next) =>
//{
//    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
//    if (string.IsNullOrWhiteSpace(correlationId))
//    {
//        correlationId = Guid.NewGuid().ToString("N");
//    }

//    context.Response.Headers["X-Correlation-Id"] = correlationId;
//    var startedAt = Stopwatch.GetTimestamp();
//    try
//    {
//        await next();
//    }
//    finally
//    {
//        var elapsed = Stopwatch.GetElapsedTime(startedAt);
//        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Gateway.Requests");
//        logger.LogInformation("{Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms with correlation ID {CorrelationId}.", context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed.TotalMilliseconds, correlationId);
//    }
//});

//app.UseRateLimiter();
//app.UseAuthentication();
//app.UseAuthorization();
//app.MapGet("/health", () => Results.Ok(new { service = "ApiGateway", status = "Ready" })).AllowAnonymous();
//app.MapReverseProxy().RequireRateLimiting("gateway");

//app.Run();

//public sealed class JwtOptions
//{
//    public string Issuer { get; init; } = "SmartLogistics.AuthService";
//    public string Audience { get; init; } = "SmartLogistics.Api";
//    public string SigningKey { get; init; } = "f5PF60kaR7yVDSPH4kZ5ZxxI9l5Zd6fcfILS+veDfRE=";
//}

//public partial class Program;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Shared.Infrastructure;
using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging("ApiGateway");

var jwt = builder.Configuration
    .GetSection("Jwt")
    .Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.SigningKey) ||
    Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be supplied through secure configuration and contain at least 32 bytes.");
}

// ------------------------------------------------------------
// Swagger (development)
// ------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartLogistics API Gateway",
        Version = "v1",
        Description = "SmartLogistics API Gateway - development API documentation."
    });
});

// ------------------------------------------------------------
// JWT Authentication
// ------------------------------------------------------------

builder.Services.AddSingleton(jwt);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.SigningKey)),

            ClockSkew = TimeSpan.FromMinutes(jwt.AccessTokenMinutes)
        };
    });

// ------------------------------------------------------------
// Authorization
// ------------------------------------------------------------

builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(
        "authenticated",
        policy => policy.RequireAuthenticatedUser());

// ------------------------------------------------------------
// Rate Limiting
// ------------------------------------------------------------

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(
        "gateway",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
});

// ------------------------------------------------------------
// YARP
// ------------------------------------------------------------

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(
        builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseApiInfrastructure();

// ------------------------------------------------------------
// OpenAPI / Scalar
// Development only
// ------------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    //app.MapOpenApi();    
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(
                "/openapi/auth/v1/swagger.json",
                "Auth Service");
            options.SwaggerEndpoint(
            "/openapi/customers/v1/swagger.json",
            "Customer Service");

            options.SwaggerEndpoint(
                "/openapi/orders/v1/swagger.json",
                "Order Service");

            options.SwaggerEndpoint(
                "/openapi/inventory/v1/swagger.json",
                "Inventory Service");

            options.SwaggerEndpoint(
                "/openapi/warehouses/v1/swagger.json",
                "Warehouse Service");

            options.SwaggerEndpoint(
                "/openapi/deliveries/v1/swagger.json",
                "Delivery Service");

            options.RoutePrefix = "swagger";
        });
        
    }
}

// ------------------------------------------------------------
// Correlation ID
// ------------------------------------------------------------

app.Use(async (context, next) =>
{
    var correlationId =
        context.Request.Headers["X-Correlation-Id"]
            .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }

    context.Response.Headers["X-Correlation-Id"] =
        correlationId;

    var startedAt = Stopwatch.GetTimestamp();

    try
    {
        await next();
    }
    finally
    {
        var elapsed =
            Stopwatch.GetElapsedTime(startedAt);

        var logger =
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Gateway.Requests");

        logger.LogInformation(
            "{Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms with correlation ID {CorrelationId}.",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            elapsed.TotalMilliseconds,
            correlationId);
    }
});

// ------------------------------------------------------------
// Middleware
// ------------------------------------------------------------

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

// ------------------------------------------------------------
// Health
// ------------------------------------------------------------

app.MapGet(
        "/health",
        () => Results.Ok(
            new
            {
                service = "ApiGateway",
                status = "Ready"
            }))
    .AllowAnonymous();

// ------------------------------------------------------------
// YARP
// ------------------------------------------------------------

app.MapReverseProxy()
    .RequireRateLimiting("gateway");

app.Run();

public sealed class JwtOptions
{
    public string Issuer { get; init; } =
        "SmartLogistics.AuthService";

    public string Audience { get; init; } =
        "SmartLogistics.Api";
    public int AccessTokenMinutes { get; init; } = 30;
    public string SigningKey { get; init; } = "f5PF60kaR7yVDSPH4kZ5ZxxI9l5Zd6fcfILS+veDfRE=";
}

public partial class Program;