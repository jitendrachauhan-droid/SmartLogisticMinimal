using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Shared.Infrastructure;

public sealed record ApiErrorResponse(bool Success, string ErrorCode, string Message);

public sealed class ApiException(string errorCode, string message, int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}

public static class ApiInfrastructureExtensions
{
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .WriteTo.Console());
        return builder;
    }

    public static WebApplication UseApiInfrastructure(this WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        return app;
    }
}

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request was cancelled by the client. {Method} {Path} {CorrelationId}", context.Request.Method, context.Request.Path, CorrelationId(context));
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }
        }
        catch (Exception exception)
        {
            var (statusCode, errorCode, message) = exception switch
            {
                ApiException apiException => (apiException.StatusCode, apiException.ErrorCode, apiException.Message),
                KeyNotFoundException => (StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The requested resource does not exist."),
                ArgumentException => (StatusCodes.Status400BadRequest, "INVALID_REQUEST", "The request is invalid."),
                _ => (StatusCodes.Status500InternalServerError, "INTERNAL_SERVER_ERROR", "An unexpected error occurred.")
            };

            logger.LogError(exception, "Unhandled exception for {Method} {Path}. {CorrelationId}", context.Request.Method, context.Request.Path, CorrelationId(context));
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiErrorResponse(false, errorCode, message)));
            }
        }
    }

    private static string CorrelationId(HttpContext context) => context.Response.Headers["X-Correlation-Id"].FirstOrDefault() ?? "unavailable";
}

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        var startedAt = Stopwatch.GetTimestamp();
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["ServiceName"] = context.RequestServices.GetRequiredService<IHostEnvironment>().ApplicationName
        }))
        {
            logger.LogInformation("Request started: {Method} {Path} with correlation ID {CorrelationId}", context.Request.Method, context.Request.Path, correlationId);
            try
            {
                await next(context);
            }
            finally
            {
                logger.LogInformation("Request completed: {Method} {Path} returned {StatusCode} in {ProcessingTimeMs} ms with correlation ID {CorrelationId}", context.Request.Method, context.Request.Path, context.Response.StatusCode, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, correlationId);
            }
        }
    }
}
