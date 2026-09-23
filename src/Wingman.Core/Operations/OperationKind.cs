namespace Wingman.Core.Operations;

/// <summary>
/// The winget command an <see cref="OperationPlan"/> runs.
/// </summary>
public enum OperationKind
{
    Install,
    Upgrade,
    Uninstall,
}
