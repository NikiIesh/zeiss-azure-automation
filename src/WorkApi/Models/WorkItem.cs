namespace WorkApi.Models;

public record WorkItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Description { get; init; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public record CreateWorkRequest(string Description);
