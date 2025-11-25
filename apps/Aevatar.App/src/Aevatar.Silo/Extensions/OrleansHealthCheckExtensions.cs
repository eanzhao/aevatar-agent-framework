using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Runtime;

namespace Aevatar.Silo.Extensions;

/// <summary>
/// Extension methods for Orleans health check integration with ASP.NET Core
/// Provides liveness and readiness probes for Kubernetes/container orchestration
/// </summary>
public static class OrleansHealthCheckExtensions
{
    /// <summary>
    /// Add Orleans health checks to the service collection
    /// </summary>
    public static IServiceCollection AddOrleansHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<OrleansHealthCheck>("orleans", tags: new[] { "live", "ready" });
        
        return services;
    }
    
    /// <summary>
    /// Map health check endpoints for Kubernetes probes
    /// - /health/live - Liveness probe (Orleans is running)
    /// - /health/ready - Readiness probe (Orleans can accept traffic)
    /// - /health - General health status
    /// </summary>
    public static IApplicationBuilder MapOrleansHealthChecks(this IApplicationBuilder app)
    {
        // Liveness probe - Orleans is alive
        app.UseHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteMinimalPlaintext
        });
        
        // Readiness probe - Orleans is ready to accept traffic
        app.UseHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteMinimalPlaintext
        });
        
        // General health endpoint
        app.UseHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteMinimalPlaintext
        });
        
        return app;
    }
    
    /// <summary>
    /// Write minimal plaintext response for health checks
    /// </summary>
    private static Task WriteMinimalPlaintext(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain";
        
        var status = report.Status == HealthStatus.Healthy ? "Healthy" : 
                    report.Status == HealthStatus.Degraded ? "Degraded" : "Unhealthy";
        
        return context.Response.WriteAsync(status);
    }
}

/// <summary>
/// Orleans health check implementation using built-in IHealthCheckParticipant system
/// Checks if Orleans silo is healthy and ready to process requests
/// </summary>
public class OrleansHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHealthCheckParticipant> _participants;
    
    public OrleansHealthCheck(IEnumerable<IHealthCheckParticipant> participants)
    {
        _participants = participants;
    }
    
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var lastCheckTime = DateTime.UtcNow.AddMinutes(-1);
            var unhealthyParticipants = new List<string>();
            
            foreach (var participant in _participants)
            {
                if (!participant.CheckHealth(lastCheckTime, out var reason))
                {
                    unhealthyParticipants.Add(reason ?? participant.GetType().Name);
                }
            }
            
            if (unhealthyParticipants.Any())
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Orleans unhealthy: {string.Join(", ", unhealthyParticipants)}"));
            }
            
            return Task.FromResult(HealthCheckResult.Healthy("Orleans is healthy"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Orleans health check failed", ex));
        }
    }
}

