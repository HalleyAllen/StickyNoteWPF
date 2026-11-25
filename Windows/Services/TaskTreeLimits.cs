using System.Text.Json;
using StickyNoteWPF.Models;

namespace StickyNoteWPF.Services;

public static class TaskTreeLimits
{
    public const int MaximumDepth = 64;
    // Each task adds an object and a SubItems array, plus the store/list envelopes.
    public const int JsonMaximumDepth = MaximumDepth * 2 + 8;

    public static bool CanAddChild(IEnumerable<TaskItem> roots, TaskItem parent)
        => Walk(roots).Any(entry => ReferenceEquals(entry.Item, parent) && entry.Depth < MaximumDepth);

    public static void Validate(IEnumerable<TaskItem> roots)
    {
        foreach (var entry in Walk(roots))
            if (entry.Depth > MaximumDepth)
                throw new JsonException($"任务最多支持 {MaximumDepth} 层嵌套");
    }

    private static IEnumerable<(TaskItem Item, int Depth)> Walk(IEnumerable<TaskItem> roots)
    {
        var pending = new Stack<(TaskItem Item, int Depth)>(roots.Where(item => item != null).Select(item => (item, 1)));
        while (pending.TryPop(out var entry))
        {
            yield return entry;
            // Stop even for an accidentally cyclic in-memory model.
            if (entry.Depth > MaximumDepth) continue;
            foreach (var child in entry.Item.SubItems ?? [])
                if (child != null) pending.Push((child, entry.Depth + 1));
        }
    }
}
