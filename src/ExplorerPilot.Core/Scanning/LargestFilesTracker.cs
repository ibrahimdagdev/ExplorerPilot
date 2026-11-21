using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Scanning;

/// <summary>
/// Maintains the top-K largest files using a min-heap. Memory usage is O(K).
/// </summary>
public sealed class LargestFilesTracker
{
    private readonly PriorityQueue<LargestFileInfo, long> _heap;
    private readonly int _capacity;

    public LargestFilesTracker(int capacity = LargestFilesOptions.MaxTrackedFiles)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
        }

        _capacity = capacity;
        _heap = new PriorityQueue<LargestFileInfo, long>(capacity);
    }

    public int Capacity => _capacity;

    public int Count => _heap.Count;

    public void Offer(LargestFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (_heap.Count < _capacity)
        {
            _heap.Enqueue(file, file.SizeBytes);
            return;
        }

        if (!_heap.TryPeek(out _, out var smallest))
        {
            return;
        }

        // Keep existing entry when sizes are equal so ranking stays stable and O(K).
        if (file.SizeBytes <= smallest)
        {
            return;
        }

        _heap.Dequeue();
        _heap.Enqueue(file, file.SizeBytes);
    }

    public void Offer(
        string name,
        string fullPath,
        long sizeBytes,
        DateTimeOffset? lastModifiedUtc)
    {
        var extension = Path.GetExtension(name);
        Offer(new LargestFileInfo
        {
            Name = name,
            FullPath = fullPath,
            SizeBytes = sizeBytes,
            Extension = extension,
            LastModifiedUtc = lastModifiedUtc
        });
    }

    /// <summary>
    /// Returns tracked files sorted by size descending, then path ascending.
    /// </summary>
    public IReadOnlyList<LargestFileInfo> ToSortedList()
    {
        if (_heap.Count == 0)
        {
            return [];
        }

        return _heap.UnorderedItems
            .Select(static item => item.Element)
            .OrderByDescending(static f => f.SizeBytes)
            .ThenBy(static f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
