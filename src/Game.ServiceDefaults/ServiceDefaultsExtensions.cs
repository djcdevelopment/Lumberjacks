using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Game.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                // CORS_ORIGINS env var: comma-separated list of allowed origins
                // e.g. "https://myapp.azurecontainerapps.io,http://localhost:5173"
                // Falls back to localhost dev defaults if not set.
                var originsEnv = builder.Configuration["CORS_ORIGINS"]
                    ?? Environment.GetEnvironmentVariable("CORS_ORIGINS");

                var origins = !string.IsNullOrWhiteSpace(originsEnv)
                    ? originsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : new[] { "http://localhost:5173", "http://localhost:5174" };

                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        builder.Services.AddHealthChecks();
        builder.Services.AddHttpClient();

        return builder;
    }

    public static WebApplication MapServiceDefaults(this WebApplication app)
    {
        app.UseCors();
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var serviceName = app.Environment.ApplicationName
                    .Replace("Game.", "")
                    .ToLowerInvariant();
                await context.Response.WriteAsJsonAsync(new
                {
                    status = report.Status == HealthStatus.Healthy ? "ok" : "degraded",
                    service = serviceName,
                    timestamp = DateTimeOffset.UtcNow,
                });
            },
        });

        return app;
    }
}
