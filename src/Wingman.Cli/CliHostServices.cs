using Wingman.Core.Elevation;
using Wingman.Core.Notifications;
using Wingman.Core.SelfUpdate;
using Wingman.Core.Setup;

namespace Wingman.Cli;

/// <summary>
/// The host's platform services for headless commands, each null where the platform has none, so
/// <c>src/Wingman/Program.cs</c> wires them in one object on any OS. <see cref="CliContext"/>
/// carries each one to the commands.
/// </summary>
public sealed record CliHostServices
{
    /// <summary>No platform services, as off Windows; what tests and the plain overload run with.</summary>
    public static CliHostServices None { get; } = new();

    /// <summary>Registers what <c>wingman setup</c> plans.</summary>
    public ISetupExecutor? SetupExecutor { get; init; }

    /// <summary>Shows the toasts <c>check --notify</c> and <c>upgrade --notify</c> build.</summary>
    public IToastSender? ToastSender { get; init; }

    /// <summary>Starts the detached winget upgrade for <c>wingman self-update</c>.</summary>
    public ISelfUpdateStarter? SelfUpdateStarter { get; init; }

    /// <summary>
    /// Runs the tray icon until it quits, given the executable path and the data directory, and
    /// returns its exit code; returns null where there is no notification area.
    /// </summary>
    public Func<string, string, int?>? TrayRunner { get; init; }

    /// <summary>Runs the TUI in this process on the given start route and returns its exit code.</summary>
    public Func<string?, CancellationToken, Task<int>>? TuiLauncher { get; init; }

    /// <summary>
    /// Starts an argv in a new terminal window, returning false when it could not start; used by
    /// <c>wingman open</c> when this process has no console to draw in.
    /// </summary>
    public Func<string[], bool>? WindowSpawner { get; init; }

    /// <summary>Opens the elevated helper for a batch, prompting for UAC.</summary>
    public Func<CancellationToken, Task<IElevatedOperationChannel>>? ElevationFactory { get; init; }

    /// <summary>This process already runs as administrator.</summary>
    public bool ProcessIsElevated { get; init; }

    /// <summary>This process has a console window the TUI can draw in; false when started hidden.</summary>
    public bool HasConsole { get; init; } = true;

    /// <summary>The <c>wingman</c> executable that setup registers and new windows start.</summary>
    public string ExePath { get; init; } = Environment.ProcessPath ?? "wingman";
}
