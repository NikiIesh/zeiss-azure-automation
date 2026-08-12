using System.Text.Json;
using Azure.Messaging.ServiceBus;
using WorkApi.Models;

namespace WorkApi.Services;

public class ServiceBusWorker(
    ServiceBusClient client,
    WorkItemStore store,
    ILogger<ServiceBusWorker> logger) : BackgroundService
{
    private const string QueueName = "work-queue";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = client.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false
        });

        processor.ProcessMessageAsync += async args =>
        {
            var item = JsonSerializer.Deserialize<WorkItem>(args.Message.Body.ToString());
            if (item is not null)
            {
                // Simulate processing work
                await Task.Delay(500, args.CancellationToken);
                store.MarkProcessed(item.Id);
                logger.LogInformation("Processed work item {Id}", item.Id);
            }
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };

        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus processing error");
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await processor.StopProcessingAsync();
    }
}
