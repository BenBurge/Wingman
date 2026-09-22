using System.Text.Json;

namespace Wingman.Core.Bundles;

/// <summary>
/// Reads and writes <see cref="Bundle"/> as JSON in UniGetUI's on-disk format.
/// </summary>
public static class BundleSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static Bundle Read(string json) =>
        JsonSerializer.Deserialize<Bundle>(json) ?? throw new JsonException("Bundle JSON deserialized to null.");

    public static string Write(Bundle bundle) => JsonSerializer.Serialize(bundle, WriteOptions);
}
