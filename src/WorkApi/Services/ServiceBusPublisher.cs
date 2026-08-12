using System.Text.Json;
using Azure.Messaging.ServiceBus;
using WorkApi.Models;

namespace WorkApi.Services;

public class ServiceBusPublisher(ServiceBusClient client, ILogger<ServiceBusPublisher> logger)
{
    private const string QueueName = "work-queue";

    public async Task SendAsync(WorkItem item, CancellationToken ct = default)
    {
        var sender = client.CreateSender(QueueName);
        var body = JsonSerializer.Serialize(item);
        var message = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            MessageId = item.Id
        };

        await sender.SendMessageAsync(message, ct);
        logger.LogInformation("Enqueued work item {Id}", item.Id);
    }
}
