using Wingman.Core.Models;

namespace Wingman.Core.Operations;

/// <summary>
/// A single queued winget operation, with the pre/post commands and elevation requirement
/// derived from a package's <see cref="Bundles.InstallOptions"/> and the user's settings.
/// </summary>
/// <param name="RequiresElevation">The options or the installer need administrator rights; under
/// <see cref="Settings.ElevationMode.Auto"/> the operation runs through the elevated helper.</param>
/// <param name="PreCommand">Shell command to run before the operation; empty when unset.</param>
/// <param name="PostCommand">Shell command to run after the operation; empty when unset.</param>
/// <param name="AbortOnPreFail">Whether a non-zero <paramref name="PreCommand"/> exit skips the operation.</param>
/// <param name="ForceElevation">Runs the operation through the elevated helper whatever the
/// <see cref="Settings.ElevationMode"/>, because the user asked for it, as with a retry elevated.</param>
public sealed record OperationPlan(
    OperationKind Kind,
    OperationRequest Request,
    bool RequiresElevation,
    string PreCommand,
    string PostCommand,
    bool AbortOnPreFail,
    bool ForceElevation = false)
{
    public const string DowngradeLabel = "downgrade";

    /// <summary>
    /// What the queue and the batch screen call the operation: <c>install</c>, <c>upgrade</c>, or
    /// <c>uninstall</c> after <see cref="Kind"/>, or <c>downgrade</c> for an install that goes back
    /// to an older version. History records the winget command, which <see cref="Kind"/> names.
    /// </summary>
    public string Label { get; init; } = DefaultLabel(Kind);

    private static string DefaultLabel(OperationKind kind) => kind switch
    {
        OperationKind.Install => "install",
        OperationKind.Upgrade => "upgrade",
        OperationKind.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
