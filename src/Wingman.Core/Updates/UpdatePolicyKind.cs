namespace Wingman.Core.Updates;

/// <summary>
/// The four ways Wingman can treat a package's available update.
/// </summary>
public enum UpdatePolicyKind
{
    /// <summary>The default: Wingman updates the package like any other.</summary>
    Update,

    /// <summary>A blocking <c>winget pin</c> stops the package from upgrading.</summary>
    Hold,

    /// <summary>Hides one version via <c>UpdatesOptions.IgnoredVersion</c> until a newer one appears.</summary>
    SkipVersion,

    /// <summary>The app updates itself; recorded in <c>UpdatesOptions.UpdatesIgnored</c> so Wingman leaves it alone.</summary>
    Exclude,
}
