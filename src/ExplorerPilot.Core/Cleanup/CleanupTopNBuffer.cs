using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Cleanup;

/// <summary>
/// Bounded buffer that keeps the top-N candidates by potential savings
/// so early capacity pressure does not systematically drop large items.
/// </summary>
public sealed class CleanupTopNBuffer
{
    private readonly int _capacity;
    private readonly PriorityQueue<CleanupCandidate, CandidateRank> _heap = new();
    private long _sequence;
    private int _offered;

    public CleanupTopNBuffer(int capacity)
    {
        _capacity = Math.Clamp(capacity, 1, 50_000);
    }

    public int Capacity => _capacity;

    public int Count => _heap.Count;

    public int OfferedCount => _offered;

    public bool IsTruncated => _offered > _capacity;

    public void Offer(CleanupCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        _offered++;
        var rank = new CandidateRank(
            candidate.PotentialSavingsBytes,
            candidate.SizeBytes,
            Interlocked.Increment(ref _sequence));

        _heap.Enqueue(candidate, rank);
        if (_heap.Count > _capacity)
        {
            _heap.Dequeue();
        }
    }

    public List<CleanupCandidate> ToSortedList()
    {
        var list = new List<CleanupCandidate>(_heap.Count);
        foreach (var (element, _) in _heap.UnorderedItems)
        {
            list.Add(element);
        }

        list.Sort(static (a, b) =>
        {
            var cmp = b.PotentialSavingsBytes.CompareTo(a.PotentialSavingsBytes);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = b.SizeBytes.CompareTo(a.SizeBytes);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = ((int)b.Risk).CompareTo((int)a.Risk);
            if (cmp != 0)
            {
                return cmp;
            }

            return string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
        });

        return list;
    }

    private readonly record struct CandidateRank(long Savings, long Size, long Sequence) : IComparable<CandidateRank>
    {
        public int CompareTo(CandidateRank other)
        {
            var cmp = Savings.CompareTo(other.Savings);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = Size.CompareTo(other.Size);
            if (cmp != 0)
            {
                return cmp;
            }

            return Sequence.CompareTo(other.Sequence);
        }
    }
}
