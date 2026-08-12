using Azure.Identity;
using Azure.Messaging.ServiceBus;
using WorkApi.Models;
using WorkApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetry();

// Service Bus client — uses Managed Identity in Azure, connection string locally
var sbConnectionString = builder.Configuration["ServiceBus:ConnectionString"];
var sbNamespace = builder.Configuration["ServiceBus:Namespace"];

if (!string.IsNullOrEmpty(sbNamespace))
{
    builder.Services.AddSingleton(new ServiceBusClient(sbNamespace, new DefaultAzureCredential()));
}
else if (!string.IsNullOrEmpty(sbConnectionString))
{
    builder.Services.AddSingleton(new ServiceBusClient(sbConnectionString));
}
else
{
    throw new InvalidOperationException("Configure ServiceBus:Namespace (Managed Identity) or ServiceBus:ConnectionString");
}

// Application services
builder.Services.AddSingleton<WorkItemStore>();
builder.Services.AddSingleton<ServiceBusPublisher>();
builder.Services.AddHostedService<ServiceBusWorker>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<ServiceBusHealthCheck>("servicebus");

var app = builder.Build();

// Health probes
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }); // liveness: always 200
app.MapHealthChecks("/health/ready"); // readiness: checks Service Bus

// POST /api/work — enqueue a work item
app.MapPost("/api/work", async (CreateWorkRequest request, ServiceBusPublisher publisher, WorkItemStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Description))
        return Results.BadRequest(new { error = "Description is required" });

    var item = new WorkItem { Description = request.Description };
    store.Add(item);
    await publisher.SendAsync(item);

    return Results.Accepted($"/api/work/{item.Id}", item);
});

// GET /api/work — return all processed items
app.MapGet("/api/work", (WorkItemStore store) => Results.Ok(store.GetAll()));

// GET /api/work/{id} — return a single work item
app.MapGet("/api/work/{id}", (string id, WorkItemStore store) =>
{
    var item = store.Get(id);
    return item is not null ? Results.Ok(item) : Results.NotFound();
});

app.Run();
