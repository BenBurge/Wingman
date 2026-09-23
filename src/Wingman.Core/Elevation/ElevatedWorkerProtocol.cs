using System.Text.Json;
using System.Text.Json.Serialization;
using Wingman.Core.Models;
using Wingman.Core.Operations;

namespace Wingman.Core.Elevation;

/// <summary>
/// Converts <see cref="WorkerMessage"/>s to and from the newline-delimited JSON spoken over the
/// elevated helper's pipe: one camelCase JSON object per line, told apart by a <c>type</c> field
/// (<c>run</c>, <c>line</c>, <c>finished</c>, <c>shutdown</c>).
/// </summary>
public static class ElevatedWorkerProtocol
{
    private const string RunType = "run";
    private const string LineType = "line";
    private const string FinishedType = "finished";
    private const string ShutdownType = "shutdown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Returns <paramref name="message"/> as a single line of JSON. JSON escapes control
    /// characters inside strings, so the result never contains a newline.
    /// </summary>
    public static string Serialize(WorkerMessage message)
    {
        var wire = message switch
        {
            WorkerMessage.RunOperation run => new WireMessage { Type = RunType, Kind = run.Kind, Request = run.Request },
            WorkerMessage.Line line => new WireMessage { Type = LineType, Text = line.Text },
            WorkerMessage.Finished finished => new WireMessage
            {
                Type = FinishedType,
                ExitCode = finished.ExitCode,
                DurationMs = finished.DurationMs,
            },
            WorkerMessage.Shutdown => new WireMessage { Type = ShutdownType },
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Unknown worker message."),
        };

        return JsonSerializer.Serialize(wire, JsonOptions);
    }

    /// <summary>
    /// Parses one line produced by <see cref="Serialize"/>.
    /// </summary>
    /// <exception cref="FormatException">
    /// The line is not valid JSON, has an unknown <c>type</c>, or lacks a field its type needs.
    /// </exception>
    public static WorkerMessage Parse(string line)
    {
        WireMessage? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireMessage>(line, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Worker message is not valid JSON: {ex.Message}", ex);
        }

        if (wire is null)
        {
            throw new FormatException("Worker message is null.");
        }

        switch (wire.Type)
        {
            case RunType:
                if (wire.Kind is null || wire.Request?.Id is null)
                {
                    throw new FormatException("Worker message 'run' needs 'kind' and 'request.id'.");
                }

                return new WorkerMessage.RunOperation(wire.Kind.Value, wire.Request);

            case LineType:
                if (wire.Text is null)
                {
                    throw new FormatException("Worker message 'line' needs 'text'.");
                }

                return new WorkerMessage.Line(wire.Text);

            case FinishedType:
                if (wire.ExitCode is null || wire.DurationMs is null)
                {
                    throw new FormatException("Worker message 'finished' needs 'exitCode' and 'durationMs'.");
                }

                return new WorkerMessage.Finished(wire.ExitCode.Value, wire.DurationMs.Value);

            case ShutdownType:
                return new WorkerMessage.Shutdown();

            default:
                throw new FormatException($"Unknown worker message type '{wire.Type}'.");
        }
    }

    /// <summary>
    /// Every field any message uses; each message type fills only its own.
    /// </summary>
    private sealed class WireMessage
    {
        public string? Type { get; set; }

        public OperationKind? Kind { get; set; }

        public OperationRequest? Request { get; set; }

        public string? Text { get; set; }

        public int? ExitCode { get; set; }

        public long? DurationMs { get; set; }
    }
}
