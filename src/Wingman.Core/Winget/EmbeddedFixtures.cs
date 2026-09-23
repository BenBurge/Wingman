using System.Reflection;

namespace Wingman.Core.Winget;

/// <summary>
/// Reads the captured winget fixtures embedded in this assembly (see the
/// <c>EmbeddedResource</c> item in <c>Wingman.Core.csproj</c>). <see cref="FakeWingetClient"/>
/// uses these instead of reading the test project's <c>Fixtures</c> folder, so it works from the
/// published single-file exe with no test project around.
/// </summary>
internal static class EmbeddedFixtures
{
    private const string ResourcePrefix = "Wingman.Core.Fixtures.";

    /// <summary>Returns the full text of the embedded fixture named <paramref name="fileName"/>.</summary>
    public static string Read(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded fixture resource '{resourceName}' was not found in {assembly.GetName().Name}. " +
                "Check the EmbeddedResource item in Wingman.Core.csproj and the fixture's file name.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
