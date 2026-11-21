using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Scanning;

/// <summary>
/// Potential space savings: keep one unique physical copy per content group.
/// Hard-linked paths share a file identity and do not increase reclaimable bytes.
/// </summary>
public static class DuplicateSavingsCalculator
{
    public static long CalculateGroupSavings(long sizeBytes, IReadOnlyList<DuplicateFileInfo> files)
    {
        if (files.Count == 0 || sizeBytes < 0)
        {
            return 0;
        }

        var uniquePhysicalCopies = CountUniquePhysicalCopies(files);
        if (uniquePhysicalCopies <= 1)
        {
            return 0;
        }

        return checked((uniquePhysicalCopies - 1) * sizeBytes);
    }

    public static int CountUniquePhysicalCopies(IReadOnlyList<DuplicateFileInfo> files)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anonymous = 0;

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.FileIdentityKey))
            {
                anonymous++;
                continue;
            }

            identities.Add(file.FileIdentityKey);
        }

        return identities.Count + anonymous;
    }

    public static long SumGroupSavings(IEnumerable<DuplicateGroup> groups) =>
        groups.Sum(g => g.PotentialSavingsBytes);
}
