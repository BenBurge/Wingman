namespace Wingman.Cli;

/// <summary>One headless <c>wingman &lt;command&gt;</c>, registered in <see cref="CliCommands.All"/>.</summary>
public interface ICliCommand
{
    /// <summary>The token that selects the command, such as <c>check</c>.</summary>
    string Name { get; }

    /// <summary>One line for the command list in <c>wingman --help</c>.</summary>
    string Summary { get; }

    /// <summary>The syntax and options that <c>wingman help &lt;command&gt;</c> prints under the summary.</summary>
    string Usage { get; }

    /// <summary>Runs the command and returns its exit code, one of <see cref="ExitCodes"/>.</summary>
    Task<int> RunAsync(CliArgs args, CliContext context);
}
