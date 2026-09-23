using Wingman.Core.SelfUpdate;

namespace Wingman.Cli.Commands;

/// <summary>
/// <c>wingman self-update</c>: compares this build with the release winget publishes and, when it
/// is newer, starts a detached winget upgrade that replaces the executable after this process exits.
/// </summary>
internal sealed class SelfUpdateCommand : ICliCommand
{
    public string Name => "self-update";

    public string Summary => "Update Wingman through winget";

    public string Usage => """
        Usage: wingman self-update [--check]

        Looks up the latest Wingman release in winget and, when it is newer than this one, starts
        winget upgrading it in the background. Restart Wingman once winget finishes.

          --check  Only report whether an update is available; exits 10 when one is
        """;

    public IReadOnlyCollection<string> Flags => ["check"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        var check = await SelfUpdateChecker.CheckAsync(context.Client, context.Version, context.Cancel);
        var onlyChecking = args.HasFlag("check");

        if (check.AvailableVersion is null)
        {
            context.Out.WriteLine("Wingman is not installed through winget");
            return onlyChecking ? ExitCodes.Success : ExitCodes.Failure;
        }

        if (!check.IsNewerAvailable)
        {
            context.Out.WriteLine($"Wingman {check.InstalledVersion} is up to date");
            return ExitCodes.Success;
        }

        if (onlyChecking)
        {
            context.Out.WriteLine($"Wingman {check.InstalledVersion}; {check.AvailableVersion} is available");
            return ExitCodes.UpdatesAvailable;
        }

        if (context.SelfUpdateStarter is not { } starter)
        {
            context.Error.WriteLine("wingman self-update: Windows only");
            return ExitCodes.Usage;
        }

        starter.StartDetachedUpgrade();
        context.Out.WriteLine("Updating Wingman in the background; restart it when winget finishes.");
        return ExitCodes.Success;
    }
}
