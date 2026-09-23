using System.Diagnostics;
using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Implements <see cref="IWingetClient"/> by running <c>winget.exe</c> through an <see cref="IProcessRunner"/>.
/// </summary>
/// <remarks>
/// Reads capture stdout and parse it with <see cref="WingetTableParser"/>, <see cref="WingetShowParser"/>,
/// <see cref="WingetVersionsParser"/>, or <see cref="WingetPinListParser"/>. Operations that change
/// the machine stream every output line as it arrives and return it in <see cref="OperationResult.Log"/>
/// with the exit code. <see cref="WingetArguments"/> builds every command line.
/// </remarks>
public sealed class WingetCliClient : IWingetClient
{
    private const string WingetFileName = "winget";

    private readonly IProcessRunner _processRunner;

    public WingetCliClient(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<string> GetVersionAsync(CancellationToken ct)
    {
        var result = await RunAsync(WingetArguments.Version(), ct);
        return result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct)
    {
        var result = await RunAsync(WingetArguments.Search(query), ct);
        return WingetTableParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct)
    {
        var result = await RunAsync(WingetArguments.ListInstalled(), ct);
        return WingetTableParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct)
    {
        var result = await RunAsync(WingetArguments.ListUpgrades(), ct);
        return WingetTableParser.Parse(result.StandardOutput);
    }

    public async Task<PackageDetails?> ShowAsync(string id, CancellationToken ct)
    {
        var result = await RunAsync(WingetArguments.Show(id), ct);
        return WingetShowParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> ListVersionsAsync(string id, CancellationToken ct)
    {
        var result = await RunAsync(WingetArguments.ShowVersions(id), ct);
        return WingetVersionsParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<Pin>> ListPinsAsync(CancellationToken ct)
    {
        var result = await RunAsync(WingetArguments.ListPins(), ct);
        return WingetPinListParser.Parse(result.StandardOutput);
    }

    public Task<OperationResult> InstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        RunOperationAsync(WingetArguments.Install(request), output, ct);

    public Task<OperationResult> UpgradeAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        RunOperationAsync(WingetArguments.Upgrade(request), output, ct);

    public Task<OperationResult> UninstallAsync(OperationRequest request, IProgress<string> output, CancellationToken ct) =>
        RunOperationAsync(WingetArguments.Uninstall(request), output, ct);

    public Task<OperationResult> PinAsync(string id, bool blocking, string? version, CancellationToken ct) =>
        RunOperationAsync(WingetArguments.PinAdd(id, blocking, version), output: null, ct);

    public Task<OperationResult> UnpinAsync(string id, CancellationToken ct) =>
        RunOperationAsync(WingetArguments.PinRemove(id), output: null, ct);

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct) =>
        _processRunner.RunAsync(WingetFileName, arguments, ct);

    private async Task<OperationResult> RunOperationAsync(
        IReadOnlyList<string> arguments, IProgress<string>? output, CancellationToken ct)
    {
        var log = new LogRecorder(output);
        var stopwatch = Stopwatch.StartNew();
        var exitCode = await _processRunner.RunStreamingAsync(WingetFileName, arguments, log, ct);
        stopwatch.Stop();
        return new OperationResult(exitCode, exitCode == 0, stopwatch.Elapsed, log.Lines());
    }

    /// <summary>
    /// Keeps every reported line and passes it on. Reports arrive on both the stdout and the stderr
    /// thread, so recording and forwarding share one lock to keep the log and the caller's view in
    /// the same order.
    /// </summary>
    private sealed class LogRecorder : IProgress<string>
    {
        private readonly List<string> _lines = [];
        private readonly Lock _lock = new();
        private readonly IProgress<string>? _forwardTo;

        public LogRecorder(IProgress<string>? forwardTo)
        {
            _forwardTo = forwardTo;
        }

        public void Report(string value)
        {
            lock (_lock)
            {
                _lines.Add(value);
                _forwardTo?.Report(value);
            }
        }

        public string[] Lines()
        {
            lock (_lock)
            {
                return [.. _lines];
            }
        }
    }
}
