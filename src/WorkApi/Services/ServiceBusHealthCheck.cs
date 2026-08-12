using Azure.Messaging.ServiceBus;

namespace WorkApi.Services;

public class ServiceBusHealthCheck(ServiceBusClient client) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify we can create a sender (validates connectivity)
            var sender = client.CreateSender("work-queue");
            await sender.CloseAsync(cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Service Bus connection is healthy");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Service Bus connection failed", ex);
        }
    }
}
