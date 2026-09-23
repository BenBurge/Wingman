using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wingman.Cli;

/// <summary>Prints the one JSON document a command writes under <c>--json</c>: camelCase, indented, enums as strings.</summary>
internal static class JsonOutput
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Write<T>(TextWriter writer, T value) =>
        writer.WriteLine(JsonSerializer.Serialize(value, Options));
}
