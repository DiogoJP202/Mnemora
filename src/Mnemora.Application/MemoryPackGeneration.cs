using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mnemora.Application;

public sealed class GenerationOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Groq";
    public string Model { get; set; } = "openai/gpt-oss-120b";
    public string? ApiKey { get; set; }
    public int MaxUnits { get; set; } = 64;
    public int MaxSourceBytes { get; set; } = 512 * 1024;
    public int MaxDocumentBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxReportBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxSessionBytes { get; set; } = 8 * 1024 * 1024;
    public int MaxGlobalBytes { get; set; } = 16 * 1024 * 1024;
    public int MaxSessions { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 90;
    public int IdleMinutes { get; set; } = 30;
    public int LifetimeMinutes { get; set; } = 120;
    public int MaxAttemptsPerUnit { get; set; } = 2;
    public int MaxCalls { get; set; } = 80;
    public int MaxOutputTokens { get; set; } = 2_500;
    public int MaxInputTokens { get; set; } = 4_000;
    public int MaxTokens { get; set; } = 180_000;
    public int MinCallIntervalSeconds { get; set; } = 60;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationSource(string Title, string Summary);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationStartRequest(
    IReadOnlyList<GenerationSource> Sources,
    string? Attribution = null,
    string? SourceUrl = null,
    bool EditionConfirmed = false,
    bool IndexComplete = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationParagraph(string Id, string Text);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationKnownEntity(string Slug, string Type, string Name,
    string? ShortDescription, IReadOnlyList<string> Aliases);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChapterInput(string UnitSlug,
    IReadOnlyList<GenerationParagraph> Paragraphs,
    IReadOnlyList<GenerationKnownEntity> KnownEntities);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationEvidence(string ParagraphId, string Quote, string Justification);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationNewEntity(string LocalId, string Type, string Name,
    string? ShortDescription, IReadOnlyList<GenerationEvidence> Evidence);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationFact(string EntityRef, string Content, string? MemoryHint,
    string Type, IReadOnlyList<GenerationEvidence> Evidence);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationAlias(string EntityRef, string Alias,
    IReadOnlyList<GenerationEvidence> Evidence);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerationRelation(string SourceRef, string TargetRef,
    string RelationType, string? Label, IReadOnlyList<GenerationEvidence> Evidence);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChapterExtraction(
    IReadOnlyList<GenerationNewEntity> NewEntities,
    IReadOnlyList<GenerationFact> Facts,
    IReadOnlyList<GenerationAlias> Aliases,
    IReadOnlyList<GenerationRelation> Relations,
    IReadOnlyList<string> Diagnostics);

public sealed record ExtractionResult(ChapterExtraction Extraction, int InputTokens, int OutputTokens);

public interface IMemoryPackGenerationProvider
{
    int EstimateInputTokens(ChapterInput input);
    Task<ExtractionResult> ExtractAsync(ChapterInput input, CancellationToken cancellationToken);
}

public sealed class GenerationException(
    string code, string message, int statusCode = 400, int? retryAfterSeconds = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed record GenerationSourceIssue(string Path, string Code, string Message,
    string Severity = "error")
{
    public bool IsBlocking => Severity == "error";
}

public sealed record GenerationSourceParseResult(
    IReadOnlyList<GenerationSource> Sources, IReadOnlyList<GenerationSourceIssue> Issues)
{
    public bool IsValid => !Issues.Any(issue => issue.IsBlocking);
}

public static class MemoryPackGenerationSourceParser
{
    private static readonly Regex Header = new(
        @"^##[ \t]+Cap[ií]tulo[ \t]+(?<number>[0-9]+)[ \t]*[—–-][ \t]*(?<title>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex ChapterHeader = new(@"^##[ \t]+Cap[ií]tulo\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // Preserve the administrator's reading order. Numbers detect duplicate/missing
    // chapters; parsing never sorts, drops or fills in narrative material.
    public static GenerationSourceParseResult Parse(string? text)
    {
        var sources = new List<GenerationSource>();
        var issues = new List<GenerationSourceIssue>();
        var seenNumbers = new HashSet<int>();
        var summary = new StringBuilder();
        string? title = null;
        var previousNumber = 0;
        var hasPreamble = false;
        var normalized = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        void FinishChapter()
        {
            if (title is null) return;
            var content = summary.ToString().Trim();
            sources.Add(new GenerationSource(title, content));
            if (content.Length == 0)
                issues.Add(new($"sources[{sources.Count - 1}].summary", "missing_summary",
                    "Este capítulo não possui resumo. A lacuna deve ser revisada; nenhum conteúdo será inventado.",
                    "warning"));
            summary.Clear();
        }

        var lines = normalized.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var trimmed = line.Trim();
            var match = Header.Match(trimmed);
            if (match.Success && int.TryParse(match.Groups["number"].Value,
                    NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number > 0 && !string.IsNullOrWhiteSpace(match.Groups["title"].Value))
            {
                FinishChapter();
                title = match.Groups["title"].Value.Trim();
                if (!seenNumbers.Add(number))
                    issues.Add(new($"lines[{lineIndex + 1}]", "duplicate_chapter",
                        $"O capítulo {number} foi informado mais de uma vez."));
                if (previousNumber > 0 && number != previousNumber + 1)
                    issues.Add(new($"lines[{lineIndex + 1}]", "chapter_sequence",
                        "Use capítulos consecutivos na ordem de leitura. Inclua o cabeçalho de capítulos sem fonte com resumo vazio."));
                else if (previousNumber == 0 && number != 1)
                    issues.Add(new($"lines[{lineIndex + 1}]", "chapter_sequence",
                        "O índice deve começar pelo capítulo 1. Inclua os capítulos anteriores sem fonte com resumo vazio."));
                previousNumber = number;
                continue;
            }

            if (ChapterHeader.IsMatch(trimmed))
                issues.Add(new($"lines[{lineIndex + 1}]", "invalid_header",
                    "Use um cabeçalho como ## Capítulo 1 — Título, com número positivo e título."));
            if (title is null)
            {
                if (trimmed.Length > 0) hasPreamble = true;
            }
            else
            {
                if (summary.Length > 0) summary.Append('\n');
                summary.Append(line);
            }
        }
        FinishChapter();
        if (sources.Count == 0)
            issues.Add(new("sources", "missing_headers",
                "Informe ao menos um capítulo usando ## Capítulo 1 — Título."));
        else if (hasPreamble)
            issues.Add(new("sources", "text_before_first_header",
                "Há texto antes do primeiro capítulo. Vincule esse material a uma unidade antes de continuar."));
        return new(sources, issues);
    }

    public static IReadOnlyList<GenerationSourceIssue> ValidateSources(
        GenerationStartRequest request, GenerationOptions options)
    {
        var issues = new List<GenerationSourceIssue>();
        if (!request.EditionConfirmed)
            issues.Add(new("editionConfirmed", "edition_not_confirmed",
                "Confirme que as fontes pertencem à edição selecionada."));
        if (!request.IndexComplete)
            issues.Add(new("indexComplete", "incomplete_index",
                "O índice não foi confirmado como completo. Revise os capítulos ausentes antes de importar.",
                "warning"));
        if (string.IsNullOrWhiteSpace(request.Attribution))
            issues.Add(new("attribution", "missing_attribution",
                "Informe a atribuição das fontes narrativas."));
        else if (request.Attribution.Trim().Length > 300)
            issues.Add(new("attribution", "attribution_too_long",
                "Use no máximo 300 caracteres na atribuição."));
        if (!string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            if (request.SourceUrl.Trim().Length > 2_000)
                issues.Add(new("sourceUrl", "source_url_too_long",
                    "Use no máximo 2000 caracteres na URL da fonte."));
            if (!Uri.TryCreate(request.SourceUrl.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host)
                || uri.UserInfo.Length > 0)
                issues.Add(new("sourceUrl", "invalid_source_url",
                    "Use uma URL HTTPS absoluta, sem credenciais."));
        }
        if (request.Sources is not { Count: > 0 })
        {
            issues.Add(new("sources", "missing_sources", "Informe ao menos uma unidade de leitura."));
            return issues;
        }
        if (request.Sources.Count > options.MaxUnits)
        {
            issues.Add(new("sources", "too_many_units",
                $"Informe no máximo {options.MaxUnits} unidades nesta geração."));
            return issues;
        }

        // Count the actual UTF-8 title and summary bytes, not UTF-16 characters.
        // The endpoint separately bounds the complete HTTP request body.
        long sourceBytes = 0;
        for (var index = 0; index < request.Sources.Count; index++)
        {
            var source = request.Sources[index];
            var path = $"sources[{index}]";
            if (source is null)
            {
                issues.Add(new(path, "invalid_source", "Informe título e resumo da unidade."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(source.Title))
                issues.Add(new($"{path}.title", "missing_title", "Informe o título da unidade."));
            else if (source.Title.Trim().Length > 300)
                issues.Add(new($"{path}.title", "title_too_long",
                    "Use no máximo 300 caracteres no título da unidade."));
            if (string.IsNullOrWhiteSpace(source.Summary))
                issues.Add(new($"{path}.summary", "missing_summary",
                    "Esta unidade não possui fonte narrativa suficiente. Revise a lacuna antes de importar.",
                    "warning"));
            sourceBytes += Encoding.UTF8.GetByteCount(source.Title ?? "")
                + (long)Encoding.UTF8.GetByteCount(source.Summary ?? "");
        }
        if (sourceBytes > options.MaxSourceBytes)
            issues.Add(new("sources", "sources_too_large",
                $"As fontes devem ter no máximo {options.MaxSourceBytes} bytes em UTF-8."));
        return issues;
    }
}
