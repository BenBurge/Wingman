using Wingman.Core.Models;

namespace Wingman.Core.Winget;

/// <summary>
/// Builds the argument list for each winget command <see cref="WingetCliClient"/> runs.
/// </summary>
public static class WingetArguments
{
    private static readonly string[] CommonFlags = ["--disable-interactivity", "--accept-source-agreements"];

    /// <summary>
    /// winget rejects the common flags alongside <c>--version</c>, so this is the one command
    /// without them.
    /// </summary>
    public static string[] Version() => ["--version"];

    public static string[] Search(string query) => WithCommonFlags(["search", query]);

    public static string[] ListInstalled() => WithCommonFlags(["list"]);

    public static string[] ListUpgrades() => WithCommonFlags(["upgrade", "--include-unknown"]);

    public static string[] Show(string id) => WithCommonFlags(["show", "--id", id, "--exact"]);

    public static string[] ShowVersions(string id) => WithCommonFlags(["show", "--id", id, "--exact", "--versions"]);

    public static string[] ListPins() => WithCommonFlags(["pin", "list"]);

    public static string[] Install(OperationRequest request) => InstallOrUpgrade("install", request);

    public static string[] Upgrade(OperationRequest request) => InstallOrUpgrade("upgrade", request);

    public static string[] Uninstall(OperationRequest request)
    {
        // winget uninstall rejects --architecture, --ignore-security-hash, and
        // --accept-package-agreements, so they are left out even when the request sets them.
        var arguments = new List<string> { "uninstall", "--id", request.Id, "--exact" };
        AddOption(arguments, "--version", request.Version);
        AddOption(arguments, "--scope", request.Scope);
        AddFlag(arguments, "--interactive", request.Interactive);
        AddFlag(arguments, "--force", request.Force);
        arguments.AddRange(CommonFlags);
        arguments.AddRange(request.CustomArguments ?? []);
        return [.. arguments];
    }

    public static string[] PinAdd(string id, bool blocking, string? version)
    {
        var arguments = new List<string> { "pin", "add", "--id", id, "--exact" };
        AddFlag(arguments, "--blocking", blocking);
        AddOption(arguments, "--version", version);
        arguments.AddRange(CommonFlags);
        return [.. arguments];
    }

    public static string[] PinRemove(string id) => WithCommonFlags(["pin", "remove", "--id", id, "--exact"]);

    private static string[] InstallOrUpgrade(string command, OperationRequest request)
    {
        var arguments = new List<string> { command, "--id", request.Id, "--exact" };
        AddOption(arguments, "--version", request.Version);
        AddOption(arguments, "--scope", request.Scope);
        AddOption(arguments, "--architecture", request.Architecture);
        AddFlag(arguments, "--interactive", request.Interactive);
        AddFlag(arguments, "--ignore-security-hash", request.SkipHashCheck);
        AddFlag(arguments, "--force", request.Force);
        arguments.AddRange(CommonFlags);
        arguments.Add("--accept-package-agreements");
        arguments.AddRange(request.CustomArguments ?? []);
        return [.. arguments];
    }

    private static string[] WithCommonFlags(string[] arguments) => [.. arguments, .. CommonFlags];

    private static void AddOption(List<string> arguments, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            arguments.Add(name);
            arguments.Add(value);
        }
    }

    private static void AddFlag(List<string> arguments, string name, bool isSet)
    {
        if (isSet)
        {
            arguments.Add(name);
        }
    }
}
