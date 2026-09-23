namespace Wingman.Core.Models;

/// <summary>
/// What to install, upgrade, or uninstall, and the options to pass to winget for it.
/// </summary>
/// <param name="Version">A specific version to target; null for winget's default.</param>
/// <param name="Scope">winget's <c>--scope</c> value (<c>user</c> or <c>machine</c>); null to let winget choose.</param>
/// <param name="Architecture">winget's <c>--architecture</c> value, such as <c>x64</c>; null to let winget choose.</param>
/// <param name="Interactive">Shows the installer's own UI instead of running it silently.</param>
/// <param name="SkipHashCheck">Installs even when the installer's hash does not match the manifest.</param>
/// <param name="Force">Passes <c>--force</c>, which overrides winget's safety checks.</param>
/// <param name="CustomArguments">Extra arguments appended verbatim after everything else.</param>
public sealed record OperationRequest(
    string Id,
    string? Version = null,
    string? Scope = null,
    string? Architecture = null,
    bool Interactive = false,
    bool SkipHashCheck = false,
    bool Force = false,
    IReadOnlyList<string>? CustomArguments = null);
