using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Implements <see cref="IWingetClient"/> by running <c>winget.exe</c> through an <see cref="IProcessRunner"/>
/// and parsing its stdout with <see cref="WingetTableParser"/>.
/// </summary>
public sealed class WingetCliClient : IWingetClient
{
    private const string WingetFileName = "winget";

    private static readonly string[] CommonFlags = ["--disable-interactivity", "--accept-source-agreements"];

    private readonly IProcessRunner _processRunner;

    public WingetCliClient(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<PackageRow>> SearchAsync(string query, CancellationToken ct)
    {
        var result = await RunAsync(["search", query], ct);
        return WingetTableParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<PackageRow>> ListInstalledAsync(CancellationToken ct)
    {
        var result = await RunAsync(["list"], ct);
        return WingetTableParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<PackageRow>> ListUpgradesAsync(CancellationToken ct)
    {
        var result = await RunAsync(["upgrade"], ct);
        return WingetTableParser.Parse(result.StandardOutput);
    }

    private Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var fullArguments = new List<string>(arguments.Count + CommonFlags.Length);
        fullArguments.AddRange(arguments);
        fullArguments.AddRange(CommonFlags);
        return _processRunner.RunAsync(WingetFileName, fullArguments, ct);
    }
}
