using Wingman.Core.Models;

namespace Wingman.Core.Operations;

/// <summary>
/// A single queued winget operation, with the pre/post commands and elevation requirement
/// derived from a package's <see cref="Bundles.InstallOptions"/> and the user's settings.
/// </summary>
/// <param name="PreCommand">Shell command to run before the operation; empty when unset.</param>
/// <param name="PostCommand">Shell command to run after the operation; empty when unset.</param>
/// <param name="AbortOnPreFail">Whether a non-zero <paramref name="PreCommand"/> exit skips the operation.</param>
public sealed record OperationPlan(
    OperationKind Kind,
    OperationRequest Request,
    bool RequiresElevation,
    string PreCommand,
    string PostCommand,
    bool AbortOnPreFail);
