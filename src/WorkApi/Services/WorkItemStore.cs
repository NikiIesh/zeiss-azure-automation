using System.Collections.Concurrent;
using WorkApi.Models;

namespace WorkApi.Services;

public class WorkItemStore
{
    private readonly ConcurrentDictionary<string, WorkItem> _items = new();

    public WorkItem Add(WorkItem item)
    {
        _items[item.Id] = item;
        return item;
    }

    public WorkItem? Get(string id) => _items.GetValueOrDefault(id);

    public IReadOnlyList<WorkItem> GetAll() => _items.Values.OrderByDescending(w => w.CreatedAt).ToList();

    public void MarkProcessed(string id)
    {
        if (_items.TryGetValue(id, out var item))
        {
            item.Status = "Processed";
            item.ProcessedAt = DateTime.UtcNow;
        }
    }
}
