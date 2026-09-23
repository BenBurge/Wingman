namespace Wingman.Core.Tests;

/// <summary>
/// Reads captured winget fixtures from the test output's Fixtures folder, where the csproj
/// copies <c>Fixtures/**</c> with <c>PreserveNewest</c>.
/// </summary>
internal static class Fixtures
{
    public static string DirectoryPath { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string Load(string fileName) => File.ReadAllText(Path.Combine(DirectoryPath, fileName));
}
