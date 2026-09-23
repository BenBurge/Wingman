using System.Text;
using Wingman.Core.Bundles;

namespace Wingman.Cli.Commands;

/// <summary><c>wingman export</c>: writes every installed package to a UniGetUI bundle.</summary>
internal sealed class ExportCommand : ICliCommand
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string Name => "export";

    public string Summary => "Export installed packages to a UniGetUI bundle";

    public string Usage => """
        Usage: wingman export <file> [--no-options] [--json]

        Writes every installed package to a UniGetUI bundle (.ubundle), with each package's stored
        install and update options. Packages winget cannot reinstall are listed as incompatible.

          --no-options  Leave the install and update options out
          --json        Print the file and counts as one JSON document
        """;

    public IReadOnlyCollection<string> Flags => ["no-options"];

    public async Task<int> RunAsync(CliArgs args, CliContext context)
    {
        if (args.Positionals.Count == 0)
        {
            context.Error.WriteLine("wingman export: name the file to write");
            context.Error.WriteLine();
            context.Error.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(args.Positionals[0]));
        var includeOptions = !args.HasFlag("no-options");

        var installed = await context.Client.ListInstalledAsync(context.Cancel);
        var bundle = BundleExporter.Build(installed, context.Options, _ => true, includeOptions, includeOptions);

        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, BundleSerializer.Write(bundle), Utf8NoBom);

        var exported = bundle.Packages.Count;
        var skipped = bundle.IncompatiblePackages.Count;
        if (context.Json)
        {
            JsonOutput.Write(context.Out, new { file = path, exported, skipped });
            return ExitCodes.Success;
        }

        var message = $"Exported {exported} packages to {path}";
        if (skipped > 0)
        {
            message += $" ({skipped} skipped: not from winget)";
        }

        context.Out.WriteLine(message);
        return ExitCodes.Success;
    }
}
