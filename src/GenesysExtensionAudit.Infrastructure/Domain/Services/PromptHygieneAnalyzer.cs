using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Analyzes Architect prompt resources to detect prompts that cannot produce audio.
/// A prompt with no usable audio will cause callers to hear silence when a flow node
/// plays that prompt, or the flow may throw an error depending on error-handling config.
/// </summary>
public sealed class PromptHygieneAnalyzer
{
    /// <summary>
    /// Inspects each prompt for missing or empty language resources.
    /// System prompts are checked the same as customer-created prompts.
    /// </summary>
    /// <param name="prompts">All prompts fetched from GET /api/v2/architect/prompts.</param>
    /// <returns>
    /// Findings for prompts that have no language resources at all
    /// (<see cref="PromptHygieneCode.NoResources"/>) or that have resource slots
    /// where every slot is missing both a media file and a TTS fallback string
    /// (<see cref="PromptHygieneCode.NoPlayableMedia"/>).
    /// </returns>
    public IReadOnlyList<PromptHygieneFinding> Analyze(IReadOnlyList<PromptDto> prompts)
    {
        var findings = new List<PromptHygieneFinding>();

        foreach (var p in prompts)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
                continue;

            var resources = p.Resources ?? [];
            bool isSystem = p.SystemPrompt == true;

            if (resources.Count == 0)
            {
                findings.Add(new PromptHygieneFinding(
                    PromptId: p.Id!,
                    PromptName: p.Name,
                    Description: p.Description,
                    IsSystemPrompt: isSystem,
                    ResourceCount: 0,
                    AffectedLanguages: "(none)",
                    FindingCode: PromptHygieneCode.NoResources,
                    Issue: $"Prompt '{p.Name}' has no language resources configured. Any flow node that plays this prompt will produce silence.",
                    Severity: FindingSeverity.High,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Upload at least one language resource (audio file or TTS string) or remove the prompt and any flow references to it."));
                continue;
            }

            // Check if every resource slot lacks both media and TTS
            var emptyResources = resources
                .Where(r => string.IsNullOrWhiteSpace(r.MediaUri) && string.IsNullOrWhiteSpace(r.TtsString))
                .ToList();

            if (emptyResources.Count == resources.Count)
            {
                var languages = string.Join(", ",
                    resources.Select(r => r.Language ?? "?").Distinct(StringComparer.OrdinalIgnoreCase).Order());

                findings.Add(new PromptHygieneFinding(
                    PromptId: p.Id!,
                    PromptName: p.Name,
                    Description: p.Description,
                    IsSystemPrompt: isSystem,
                    ResourceCount: resources.Count,
                    AffectedLanguages: languages,
                    FindingCode: PromptHygieneCode.NoPlayableMedia,
                    Issue: $"Prompt '{p.Name}' has {resources.Count} language resource(s) ({languages}) but none have an audio file or TTS string. Callers will hear silence.",
                    Severity: FindingSeverity.Medium,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Upload an audio recording or add a TTS string for each language configured on this prompt."));
            }
        }

        return findings;
    }
}
