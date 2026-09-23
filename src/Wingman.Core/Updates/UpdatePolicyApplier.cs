using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Options;
using Wingman.Core.Winget;

namespace Wingman.Core.Updates;

/// <summary>
/// Carries out an <see cref="UpdatePolicyKind"/> decision: pins or unpins the package with winget
/// as needed, then updates its stored <see cref="UpdatesOptions"/> to match.
/// </summary>
public sealed class UpdatePolicyApplier
{
    private readonly IWingetClient _client;
    private readonly PackageOptionsStore _options;

    public UpdatePolicyApplier(IWingetClient client, PackageOptionsStore options)
    {
        _client = client;
        _options = options;
    }

    /// <param name="note">
    /// Unused: UniGetUI's <c>UpdatesOptions</c> schema has no field for a note, so there is nowhere
    /// yet to store one. Accepted so the TUI's call site has a stable signature once that field exists.
    /// </param>
    public async Task<OperationResult> ApplyAsync(
        PackageRow row, UpdatePolicyKind kind, string? note, IReadOnlyList<Pin> currentPins, CancellationToken ct)
    {
        var hasBlockingPin = currentPins.Any(pin =>
            string.Equals(pin.Id, row.Id, StringComparison.OrdinalIgnoreCase) && pin.PinType == PinType.Blocking);

        OperationResult? pinResult = null;
        if (kind == UpdatePolicyKind.Hold)
        {
            if (!hasBlockingPin)
            {
                pinResult = await _client.PinAsync(row.Id, blocking: true, version: null, ct);
            }
        }
        else if (hasBlockingPin)
        {
            pinResult = await _client.UnpinAsync(row.Id, ct);
        }

        if (pinResult is { Succeeded: false })
        {
            return pinResult;
        }

        var updatesOptions = kind switch
        {
            UpdatePolicyKind.SkipVersion => new UpdatesOptions { IgnoredVersion = row.AvailableVersion ?? "" },
            UpdatePolicyKind.Exclude => new UpdatesOptions { UpdatesIgnored = true },
            _ => new UpdatesOptions(),
        };
        _options.SetUpdatesOptions(row.Id, updatesOptions);

        return pinResult ?? new OperationResult(0, true, TimeSpan.Zero, []);
    }
}
