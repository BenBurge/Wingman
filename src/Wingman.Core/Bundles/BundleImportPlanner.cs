using Wingman.Core.Models;

namespace Wingman.Core.Bundles;

/// <summary>
/// Counts of an import plan's rows, one bucket per <see cref="ImportAction"/>.
/// </summary>
public sealed record ImportSummary(int Install, int Upgrade, int Keep, int Skip, int Incompatible);

/// <summary>
/// Plans what an imported <see cref="Bundle"/> would do against the current install, without
/// changing anything; the batch runner acts on the rows the caller keeps selected.
/// </summary>
public static class BundleImportPlanner
{
    public static IReadOnlyList<ImportPlanRow> Plan(
        Bundle bundle, IReadOnlyList<PackageRow> installedRows, IReadOnlyList<PackageRow> upgradeRows)
    {
        var installedById = new Dictionary<string, PackageRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in installedRows)
        {
            installedById[row.Id] = row;
        }

        var upgradeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in upgradeRows)
        {
            upgradeIds.Add(row.Id);
        }

        var plan = new List<ImportPlanRow>(bundle.Packages.Count + bundle.IncompatiblePackages.Count);

        foreach (var package in bundle.Packages)
        {
            plan.Add(PlanPackage(package, installedById, upgradeIds));
        }

        foreach (var incompatible in bundle.IncompatiblePackages)
        {
            var package = new BundlePackage
            {
                Id = incompatible.Id,
                Name = incompatible.Name,
                Version = incompatible.Version,
                Source = incompatible.Source,
            };
            plan.Add(new ImportPlanRow(package, ImportAction.Incompatible, "not from winget", ""));
        }

        return plan;
    }

    public static ImportSummary Summarize(IReadOnlyList<ImportPlanRow> plan)
    {
        var install = 0;
        var upgrade = 0;
        var keep = 0;
        var skip = 0;
        var incompatible = 0;

        foreach (var row in plan)
        {
            switch (row.Action)
            {
                case ImportAction.Install:
                    install++;
                    break;
                case ImportAction.Upgrade:
                    upgrade++;
                    break;
                case ImportAction.Keep:
                    keep++;
                    break;
                case ImportAction.Skip:
                    skip++;
                    break;
                case ImportAction.Incompatible:
                    incompatible++;
                    break;
            }
        }

        return new ImportSummary(install, upgrade, keep, skip, incompatible);
    }

    private static ImportPlanRow PlanPackage(
        BundlePackage package,
        IReadOnlyDictionary<string, PackageRow> installedById,
        IReadOnlySet<string> upgradeIds)
    {
        if (!string.Equals(package.ManagerName, "WinGet", StringComparison.OrdinalIgnoreCase))
        {
            return new ImportPlanRow(package, ImportAction.Incompatible, $"{package.ManagerName} package", "");
        }

        if (!installedById.TryGetValue(package.Id, out var installedRow))
        {
            return new ImportPlanRow(package, ImportAction.Install, "not installed", "");
        }

        var installedVersion = installedRow.Version;

        var isAlreadyInstalled =
            package.Version.Length == 0
            || string.Equals(package.Version, "latest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(package.Version, installedVersion, StringComparison.Ordinal);
        if (isAlreadyInstalled)
        {
            return new ImportPlanRow(package, ImportAction.Keep, "already installed", installedVersion);
        }

        if (upgradeIds.Contains(package.Id))
        {
            return new ImportPlanRow(package, ImportAction.Upgrade, "upgrade available", installedVersion);
        }

        if (CompareVersionsNatural(package.Version, installedVersion) > 0)
        {
            return new ImportPlanRow(package, ImportAction.Upgrade, $"bundle has {package.Version}", installedVersion);
        }

        return new ImportPlanRow(package, ImportAction.Keep, $"installed {installedVersion} is newer", installedVersion);
    }

    // Winget versions are not strict semver (segment counts vary, some segments are non-numeric),
    // so this compares numeric segments as numbers and everything else ordinally, segment by
    // segment, which is the same rule UniGetUI itself uses to sort versions.
    private static int CompareVersionsNatural(string left, string right)
    {
        var leftSegments = left.Split('.');
        var rightSegments = right.Split('.');
        var segmentCount = Math.Max(leftSegments.Length, rightSegments.Length);

        for (var i = 0; i < segmentCount; i++)
        {
            var leftSegment = i < leftSegments.Length ? leftSegments[i] : "";
            var rightSegment = i < rightSegments.Length ? rightSegments[i] : "";

            var comparison =
                int.TryParse(leftSegment, out var leftNumber) && int.TryParse(rightSegment, out var rightNumber)
                    ? leftNumber.CompareTo(rightNumber)
                    : string.CompareOrdinal(leftSegment, rightSegment);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
