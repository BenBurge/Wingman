using Wingman.Core.Models;
using Wingman.Core.Options;

namespace Wingman.Core.Bundles;

/// <summary>
/// Builds a UniGetUI-compatible <see cref="Bundle"/> from the currently installed packages.
/// </summary>
/// <remarks>
/// Holds (winget pins) have no field in UniGetUI's bundle schema, so a pinned package still
/// exports as an ordinary entry; the pin itself is never written to the bundle.
/// </remarks>
public static class BundleExporter
{
    private const string IncompatiblePackagesMessage =
        "The following packages could not be exported because they are not from a supported package manager.";

    public static Bundle Build(
        IReadOnlyList<PackageRow> installedRows,
        PackageOptionsStore options,
        Func<PackageRow, bool> isSelected,
        bool includeOptions,
        bool includeUpdatesOptions)
    {
        var bundle = new Bundle();

        foreach (var row in installedRows)
        {
            if (!isSelected(row))
            {
                continue;
            }

            if (!IsCompatible(row))
            {
                bundle.IncompatiblePackages.Add(new IncompatiblePackage
                {
                    Id = row.Id,
                    Name = row.Name,
                    Version = row.Version,
                    Source = row.Source,
                });
                continue;
            }

            var installOptions = options.GetInstallOptions(row.Id);
            var updatesOptions = options.GetUpdatesOptions(row.Id);

            bundle.Packages.Add(new BundlePackage
            {
                Id = row.Id,
                Name = row.Name,
                Version = row.Version,
                Source = "winget",
                ManagerName = "WinGet",
                InstallationOptions = includeOptions && !installOptions.IsDefault() ? installOptions : null,
                Updates = includeUpdatesOptions && !updatesOptions.IsDefault() ? updatesOptions : null,
            });
        }

        if (bundle.IncompatiblePackages.Count > 0)
        {
            bundle.IncompatiblePackagesInfo = IncompatiblePackagesMessage;
        }

        return bundle;
    }

    /// <summary>
    /// True when <paramref name="row"/> came from winget and can be exported; used by the UI to
    /// show the "skipped · not from winget" note on rows it will not export.
    /// </summary>
    public static bool IsCompatible(PackageRow row) =>
        string.Equals(row.Source, "winget", StringComparison.OrdinalIgnoreCase);
}
