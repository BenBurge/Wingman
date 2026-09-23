using System.Runtime.Versioning;
using Wingman.Core.Notifications;
using Wingman.Core.Winget;

namespace Wingman.Windows.Notifications;

/// <summary>
/// Shows toasts by running the PowerShell command <see cref="ToastBuilder"/> builds, which reaches
/// the WinRT toast API without this project taking a Windows SDK dependency. Windows shows the
/// toast only once <c>wingman setup</c> has created the Start Menu shortcut carrying
/// <see cref="AppUserModelId"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ToastNotifier : IToastSender
{
    public const string AppUserModelId = "BenBurge.Wingman";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly IProcessRunner _runner;

    public ToastNotifier(IProcessRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Shows <paramref name="content"/>, writing any failure to standard error instead of
    /// throwing, so a toast can never fail the command that sent it. Only a cancellation of
    /// <paramref name="ct"/> itself propagates.
    /// </summary>
    public async Task SendAsync(ToastContent content, CancellationToken ct)
    {
        var argv = ToastBuilder.BuildPowerShellCommand(content, AppUserModelId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            var result = await _runner.RunAsync(WindowsPowerShellPath, argv[1..], timeout.Token);
            if (result.ExitCode != 0)
            {
                ReportFailure(FirstNonBlankLine(result.StandardError) ?? $"powershell exited with code {result.ExitCode}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ReportFailure($"timed out after {Timeout.TotalSeconds:0} s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportFailure(ex.Message);
        }
    }

    /// <summary>
    /// Windows PowerShell 5.1, not a bare <c>powershell.exe</c> resolved off PATH: the WinRT toast
    /// types <see cref="ToastBuilder"/> activates are not loadable from PowerShell 7+ (<c>pwsh</c>),
    /// which does not host the WinRT activation factories. In Constrained Language mode those WinRT
    /// types are blocked outright, and the toast then fails harmlessly through the same non-zero
    /// exit code path above.
    /// </summary>
    private static string WindowsPowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    private static void ReportFailure(string reason) =>
        Console.Error.WriteLine($"wingman: toast failed: {reason}");

    private static string? FirstNonBlankLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }
}
