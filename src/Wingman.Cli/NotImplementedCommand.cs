namespace Wingman.Cli;

/// <summary>Holds a command's name and summary in the registry until its own issue implements it.</summary>
internal sealed class NotImplementedCommand : ICliCommand
{
    public NotImplementedCommand(string name, string summary)
    {
        Name = name;
        Summary = summary;
        Usage = $"Usage: wingman {name} [options]";
    }

    public string Name { get; }

    public string Summary { get; }

    public string Usage { get; }

    public Task<int> RunAsync(CliArgs args, CliContext context)
    {
        context.Error.WriteLine($"wingman {Name}: not implemented yet (see the Phase 3 issues)");
        return Task.FromResult(ExitCodes.Usage);
    }
}
