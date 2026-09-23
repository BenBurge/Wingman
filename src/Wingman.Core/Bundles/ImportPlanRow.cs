namespace Wingman.Core.Bundles;

/// <summary>
/// One row of a plan built by <see cref="BundleImportPlanner"/>: what it proposes to do with a
/// bundle package, and why.
/// </summary>
/// <param name="InstalledVersion">
/// The currently installed version, or <c>""</c> when <paramref name="Package"/> is not installed.
/// </param>
public sealed record ImportPlanRow(BundlePackage Package, ImportAction Action, string Reason, string InstalledVersion);
