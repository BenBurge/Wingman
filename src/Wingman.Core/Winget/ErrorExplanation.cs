using Wingman.Core.Updates;

namespace Wingman.Core.Winget;

/// <summary>
/// A plain-language decoding of a winget or MSI exit code, shown when an operation fails.
/// </summary>
/// <param name="Code">The exit code as winget or the installer reports it, formatted by <see cref="WingetErrorCodes.Format"/>.</param>
/// <param name="Name">The underlying constant's name, such as <c>INSTALLER_HASH_MISMATCH</c> or <c>ERROR_INSTALL_FAILURE</c>.</param>
/// <param name="WingetSaid">The message winget or the installer typically prints for this code.</param>
/// <param name="UsuallyMeans">What normally causes this code, in plain language.</param>
/// <param name="Suggestion">What to try next.</param>
/// <param name="SuggestedPolicy">The update policy to offer as the default choice, when one fits.</param>
public sealed record ErrorExplanation(
    string Code,
    string Name,
    string WingetSaid,
    string UsuallyMeans,
    string Suggestion,
    UpdatePolicyKind? SuggestedPolicy);
