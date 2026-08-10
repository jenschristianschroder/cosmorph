using System.Text.Json;
using System.Text.Json.Serialization;
using Cosmorph.Domain.Events;

namespace Cosmorph.Application.Worldmind;

/// <summary>Strict transport DTO for a Worldmind response. Unknown members are rejected.</summary>
public sealed record WorldmindResponseDocument
{
    [JsonPropertyName("selectedCandidateId")]
    public string? SelectedCandidateId { get; init; }

    [JsonPropertyName("ranking")]
    public string[]? Ranking { get; init; }

    [JsonPropertyName("narration")]
    public string? Narration { get; init; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; init; }
}

/// <summary>
/// Parses and validates a Worldmind response before it can reach the domain. The model can only ever
/// select an engine-created candidate identifier and supply short text.
/// </summary>
public static class WorldmindResponseParser
{
    /// <summary>JSON schema handed to the model when structured output is supported.</summary>
    public const string JsonSchema = """
        {
          "type": "object",
          "properties": {
            "selectedCandidateId": { "type": "string" },
            "ranking": { "type": "array", "items": { "type": "string" } },
            "narration": { "type": "string" },
            "rationale": { "type": "string" }
          },
          "required": ["selectedCandidateId", "ranking", "narration", "rationale"],
          "additionalProperties": false
        }
        """;

    public const int MaxResponseCharacters = 4_000;

    private static readonly JsonSerializerOptions StrictOptions = new(JsonSerializerDefaults.General)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8,
    };

    public static bool TryParse(
        string? content,
        DecisionRequest request,
        out GameMasterDecision? decision,
        out DecisionValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        decision = null;

        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxResponseCharacters)
        {
            result = DecisionValidationResult.RejectedInvalidSchema;
            return false;
        }

        WorldmindResponseDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WorldmindResponseDocument>(content, StrictOptions);
        }
        catch (JsonException)
        {
            result = DecisionValidationResult.RejectedInvalidSchema;
            return false;
        }

        if (document?.SelectedCandidateId is null || document.Narration is null)
        {
            result = DecisionValidationResult.RejectedInvalidSchema;
            return false;
        }

        var selected = document.SelectedCandidateId;
        if (request.Candidates.All(c => !string.Equals(c.Id, selected, StringComparison.Ordinal)))
        {
            result = DecisionValidationResult.RejectedUnknownCandidate;
            return false;
        }

        var ranking = (document.Ranking ?? [])
            .Where(id => request.Candidates.Any(c => string.Equals(c.Id, id, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(request.Candidates.Count)
            .ToArray();

        var narration = Sanitize(document.Narration, GameMasterDecision.MaxNarrationLength);
        if (narration.Length == 0)
        {
            result = DecisionValidationResult.RejectedInvalidSchema;
            return false;
        }

        decision = new GameMasterDecision
        {
            SelectedCandidateId = selected,
            Ranking = ranking,
            Narration = narration,
            Rationale = Sanitize(document.Rationale ?? string.Empty, GameMasterDecision.MaxRationaleLength),
            IsFallback = false,
        };

        result = DecisionValidationResult.Accepted;
        return true;
    }

    /// <summary>
    /// Model text is data, never markup or instructions. Control characters are removed and the
    /// result is truncated; rendering layers still encode it.
    /// </summary>
    public static string Sanitize(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        Span<char> buffer = stackalloc char[Math.Min(value.Length, maxLength)];
        var written = 0;
        foreach (var c in value)
        {
            if (written == buffer.Length)
            {
                break;
            }

            if (char.IsControl(c) && c is not ' ')
            {
                continue;
            }

            buffer[written++] = c;
        }

        return new string(buffer[..written]).Trim();
    }
}
