using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mnemora.Application;
using Mnemora.Domain;

namespace Mnemora.Infrastructure;

// Pure, append-only assembly: the session commits this result only after all limits pass.
public sealed class MemoryPackGenerationAssembler(GenerationOptions? options = null)
{
    private readonly GenerationOptions limits = options ?? new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex ParagraphBreak = new(@"\n[ \t]*\n+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Retrospective = new(
        @"\b(futur[oa]s? (traidor[a]?|rainha|rei|vila[oa])|vila[oa] disfarcad[oa]|no final|mais tarde se revela|revelad[oa] posteriormente|future (traitor|queen|king|villain)|eventually revealed)\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Uncertainty = new(
        @"\b(suspeit[a-z]*|acredit[a-z]*|supoe|parece|sem prova|nao confirma|suspects?|believes?|unconfirmed)\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Attribution = new(
        @"\b(suspeit[a-z]*|acredit[a-z]*|supoe|parece|nao|segundo|afirma|alega|suspects?|believes?|unconfirmed|claims?)\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public MemoryPackDocument Create(MemoryPackBook book, GenerationStartRequest start)
    {
        var issues = MemoryPackGenerationSourceParser.ValidateSources(start, limits);
        if (issues.Any(issue => issue.IsBlocking))
            throw new GenerationException("invalid_sources", "Confira as fontes, atribuição e edição antes de gerar.");
        var units = start.Sources.Select((source, index) => new MemoryPackUnit(
            $"capitulo-{index + 1:000}", null, source.Title.Trim(), $"Capítulo {index + 1}",
            "Chapter", index + 1)).ToArray();
        var document = new MemoryPackDocument(1, book,
            new MemoryPackSource(Optional(start.SourceUrl), Optional(start.Attribution)), units, [], []);
        CheckDocumentSize(document);
        return document;
    }

    public ChapterInput BuildInput(MemoryPackDocument document, GenerationSource source, int index)
    {
        if (index < 0 || index >= document.ReadingUnits.Count)
            throw new GenerationException("unknown_unit", "A unidade corrente não pertence a este índice.");
        var unit = document.ReadingUnits[index];
        if (!string.Equals(unit.Title, source.Title?.Trim(), StringComparison.Ordinal))
            throw new GenerationException("source_unit_mismatch", "A fonte não corresponde à unidade corrente.");
        var text = (source.Summary ?? "").Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Trim();
        var paragraphs = text.Length == 0 ? [] : ParagraphBreak.Split(text)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select((part, position) => new GenerationParagraph($"p{position + 1}", part.Trim())).ToArray();
        var order = document.ReadingUnits.ToDictionary(item => item.Slug, item => item.OrderIndex,
            StringComparer.Ordinal);
        var known = document.Entities.Where(entity =>
                order.TryGetValue(entity.FirstKnownAtUnitSlug, out var first) && first < unit.OrderIndex)
            .Select(entity => new GenerationKnownEntity(entity.Slug, entity.Type, entity.Name,
                entity.ShortDescription, entity.Aliases.Where(alias =>
                        order.TryGetValue(alias.RevealAtUnitSlug, out var reveal) && reveal < unit.OrderIndex)
                    .Select(alias => alias.Alias).ToArray())).ToArray();
        return new(unit.Slug, paragraphs, known);
    }

    public GenerationAssemblyResult Apply(MemoryPackDocument document, ChapterInput input,
        ChapterExtraction extraction, IReadOnlyList<GenerationReportEvidence>? existingEvidence = null)
    {
        if (extraction.NewEntities is null || extraction.Facts is null || extraction.Aliases is null
            || extraction.Relations is null || extraction.Diagnostics is null)
            throw new GenerationException("invalid_extraction", "A resposta não contém todas as listas de candidatos.");
        var orders = document.ReadingUnits.ToDictionary(item => item.Slug, item => item.OrderIndex,
            StringComparer.Ordinal);
        if (!orders.TryGetValue(input.UnitSlug, out var currentOrder))
            throw new GenerationException("unknown_unit", "A unidade de revelação não pertence ao documento.");
        if (input.Paragraphs is null || input.KnownEntities is null
            || input.Paragraphs.Any(paragraph => paragraph is null || string.IsNullOrWhiteSpace(paragraph.Id)
                || paragraph.Text is null)
            || input.Paragraphs.Select(paragraph => paragraph.Id).Distinct(StringComparer.Ordinal).Count()
                != input.Paragraphs.Count)
            throw new GenerationException("invalid_input", "As referências dos parágrafos são inválidas.");

        var entities = document.Entities.ToList();
        var relations = document.Relations.ToList();
        var evidence = existingEvidence?.ToList() ?? [];
        var diagnostics = new List<string>();
        var paragraphs = input.Paragraphs.ToDictionary(paragraph => paragraph.Id,
            paragraph => paragraph.Text, StringComparer.Ordinal);
        var sourceHash = Hash(string.Join("\n\n", input.Paragraphs.Select(paragraph => paragraph.Text)));
        var requestedKnown = input.KnownEntities.Where(entity => entity is not null)
            .Select(entity => entity.Slug).ToHashSet(StringComparer.Ordinal);
        var references = document.Entities.Where(entity => requestedKnown.Contains(entity.Slug)
                && orders.TryGetValue(entity.FirstKnownAtUnitSlug, out var first) && first < currentOrder)
            .ToDictionary(entity => entity.Slug, entity => entity.Slug, StringComparer.Ordinal);
        var forbiddenIds = extraction.NewEntities.Where(entity => entity is not null
                && !string.IsNullOrWhiteSpace(entity.LocalId))
            .GroupBy(entity => entity.LocalId.Trim(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1 || references.ContainsKey(group.Key))
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);

        void Warn(string message) => diagnostics.Add(message);
        bool CheckEvidence(IReadOnlyList<GenerationEvidence>? supplied, string kind,
            out GenerationEvidence[] checkedEvidence)
        {
            checkedEvidence = [];
            if (supplied is not { Count: > 0 } || supplied.Any(item => item is null
                    || !Text(item.ParagraphId, 80) || !Text(item.Quote, 300)
                    || !Text(item.Justification, 1_000)
                    || !paragraphs.TryGetValue(item.ParagraphId.Trim(), out var paragraph)
                    || !paragraph.Contains(item.Quote.Trim(), StringComparison.Ordinal)))
            {
                Warn($"{kind}: candidato descartado por evidência ausente, inválida ou citação não encontrada.");
                return false;
            }
            checkedEvidence = supplied.Select(item => new GenerationEvidence(item.ParagraphId.Trim(),
                item.Quote.Trim(), item.Justification.Trim())).ToArray();
            return true;
        }
        void Record(GenerationEvidence[] citations, string itemId, string path)
        {
            foreach (var citation in citations)
            {
                var record = new GenerationReportEvidence(input.UnitSlug, sourceHash,
                    citation.ParagraphId, citation.Quote, citation.Justification, itemId, path);
                if (!evidence.Contains(record)) evidence.Add(record);
            }
        }
        int EntityIndex(string? reference) => !string.IsNullOrWhiteSpace(reference)
            && references.TryGetValue(reference.Trim(), out var slug)
                ? entities.FindIndex(entity => entity.Slug == slug) : -1;

        if (input.Paragraphs.Count == 0)
        {
            Warn("Fonte ausente: nenhum candidato desta unidade foi incorporado.");
            return Finish();
        }
        if (input.Paragraphs.Any(paragraph => Retrospective.IsMatch(Fold(paragraph.Text))))
            Warn("A fonte contém sinais de retrospectiva ou revelação futura; revise a fronteira de conhecimento.");

        foreach (var candidate in extraction.NewEntities)
        {
            if (candidate is null || !Text(candidate.LocalId, 160) || !Text(candidate.Name, 200)
                || !OptionalText(candidate.ShortDescription, 1_000)
                || !Named<LoreEntityType>(candidate.Type, out var type))
            { Warn("Entidade: candidato descartado por nome, tipo, referência ou descrição inválidos."); continue; }
            var localId = candidate.LocalId.Trim();
            if (forbiddenIds.Contains(localId))
            { Warn("Entidade: referência local duplicada ou em conflito com entidade conhecida; candidato descartado."); continue; }
            if (!CheckEvidence(candidate.Evidence, "Entidade", out var citations)) continue;
            if (!SupportedName(candidate.Name, citations))
            { Warn("Entidade descartada: o nome ou rótulo contém termos sem suporte na evidência verificada."); continue; }
            var description = Optional(candidate.ShortDescription);
            if (description is not null && !citations.Any(citation =>
                    citation.Quote.Contains(description, StringComparison.Ordinal)))
            { description = null; Warn("Descrição inicial omitida: não é um trecho literal da evidência verificada."); }
            if (Retrospective.IsMatch(Fold(candidate.Name + " " + description + " "
                    + string.Join(" ", citations.Select(citation => citation.Quote)))))
            { Warn("Entidade retida para revisão: a apresentação contém sinais de conhecimento futuro."); continue; }
            if (entities.Count >= 500) Limit("entidades");
            var name = candidate.Name.Trim();
            if (entities.Any(entity => string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)))
                Warn("Nome repetido ou persona ambígua: as entidades permanecem separadas; revise a identidade.");
            var number = entities.Count + 1;
            string slug;
            do { slug = $"entity-{number++:0000}"; }
            while (entities.Any(entity => entity.Slug == slug));
            var entity = new MemoryPackEntity(slug, type!, name, description,
                null, input.UnitSlug, 1, null, [], []);
            entities.Add(entity);
            references.Add(localId, slug);
            Record(citations, $"entity:{slug}", $"entities[{entities.Count - 1}]");
        }

        foreach (var candidate in extraction.Facts)
        {
            if (candidate is null || !Text(candidate.Content, 4_000)
                || !Named<LoreFactType>(candidate.Type, out var type))
            { Warn("Fato: candidato descartado por texto ou tipo inválido."); continue; }
            var index = EntityIndex(candidate.EntityRef);
            if (index < 0) { Warn("Fato descartado: entidade participante desconhecida ou ambígua."); continue; }
            if (!CheckEvidence(candidate.Evidence, "Fato", out var citations)) continue;
            var content = candidate.Content.Trim();
            if (Retrospective.IsMatch(Fold(content + " "
                    + string.Join(" ", citations.Select(citation => citation.Quote)))))
            { Warn("Fato retido para revisão: o texto ou a evidência contém sinais de conhecimento futuro."); continue; }
            if (citations.Any(citation => Uncertainty.IsMatch(Fold(citation.Quote)))
                && !Attribution.IsMatch(Fold(content)))
            { Warn("Fato retido para revisão: a evidência é atribuída ou incerta, mas o texto afirma uma certeza."); continue; }
            var hint = Optional(candidate.MemoryHint);
            if (hint is not null && (hint.Length > 1_000
                    || !citations.Any(citation => citation.Quote.Contains(hint, StringComparison.Ordinal))
                    || Retrospective.IsMatch(Fold(hint))))
            { hint = null; Warn("Pista de memória omitida: não é um trecho literal seguro da evidência verificada do fato."); }
            var entity = entities[index];
            var factIndex = entity.Facts.ToList().FindIndex(fact => fact.Type == type
                && string.Equals(fact.Content, content, StringComparison.Ordinal));
            if (factIndex < 0)
            {
                if (entity.Facts.Count >= 100) Limit("fatos por entidade");
                factIndex = entity.Facts.Count;
                var fact = new MemoryPackFact(content, hint, type!, 1, input.UnitSlug);
                entities[index] = entity with { Facts = entity.Facts.Append(fact).ToArray() };
            }
            // A repeated fact adds traceability, never a new hint or an earlier reveal.
            Record(citations, ItemId("fact", entity.Slug, type!, content),
                $"entities[{index}].facts[{factIndex}]");
        }

        foreach (var candidate in extraction.Aliases)
        {
            if (candidate is null || !Text(candidate.Alias, 200))
            { Warn("Alias: candidato descartado por nome inválido."); continue; }
            var index = EntityIndex(candidate.EntityRef);
            if (index < 0) { Warn("Alias descartado: entidade participante desconhecida ou ambígua."); continue; }
            if (!CheckEvidence(candidate.Evidence, "Alias", out var citations)) continue;
            if (citations.Any(citation => Uncertainty.IsMatch(Fold(citation.Quote))))
            { Warn("Alias retido para revisão: a identidade é apenas atribuída ou incerta na evidência."); continue; }
            var alias = candidate.Alias.Trim();
            if (!citations.Any(citation => citation.Quote.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            { Warn("Alias descartado: o nome não aparece na citação verificada."); continue; }
            var entity = entities[index];
            var aliasIndex = entity.Aliases.ToList().FindIndex(item =>
                string.Equals(item.Alias, alias, StringComparison.OrdinalIgnoreCase));
            if (aliasIndex < 0)
            {
                if (entity.Aliases.Count >= 20) Limit("aliases por entidade");
                aliasIndex = entity.Aliases.Count;
                entities[index] = entity with { Aliases = entity.Aliases
                    .Append(new MemoryPackAlias(alias, input.UnitSlug)).ToArray() };
            }
            Record(citations, ItemId("alias", entity.Slug, entity.Aliases.ElementAtOrDefault(aliasIndex)?.Alias ?? alias),
                $"entities[{index}].aliases[{aliasIndex}]");
        }

        foreach (var candidate in extraction.Relations)
        {
            if (candidate is null || !Text(candidate.RelationType, 80) || !OptionalText(candidate.Label, 300))
            { Warn("Relação: candidato descartado por tipo ou rótulo inválido."); continue; }
            var sourceIndex = EntityIndex(candidate.SourceRef);
            var targetIndex = EntityIndex(candidate.TargetRef);
            if (sourceIndex < 0 || targetIndex < 0)
            { Warn("Relação descartada: participante desconhecido ou ambíguo."); continue; }
            if (!CheckEvidence(candidate.Evidence, "Relação", out var citations)) continue;
            if (citations.Any(citation => Uncertainty.IsMatch(Fold(citation.Quote))))
            { Warn("Relação retida para revisão: a evidência não confirma a relação."); continue; }
            var sourceSlug = entities[sourceIndex].Slug;
            var targetSlug = entities[targetIndex].Slug;
            var relationType = candidate.RelationType.Trim();
            var label = Optional(candidate.Label);
            var relationIndex = relations.FindIndex(item => item.SourceEntitySlug == sourceSlug
                && item.TargetEntitySlug == targetSlug && item.RelationType == relationType && item.Label == label);
            if (relationIndex < 0)
            {
                if (relations.Count >= 5_000) Limit("relações");
                relationIndex = relations.Count;
                relations.Add(new MemoryPackRelation(sourceSlug, targetSlug, relationType, label, input.UnitSlug));
            }
            Record(citations, ItemId("relation", sourceSlug, targetSlug, relationType, label ?? ""),
                $"relations[{relationIndex}]");
        }

        foreach (var diagnostic in extraction.Diagnostics)
            if (Text(diagnostic, 1_000)) Warn(diagnostic.Trim());
        return Finish();

        GenerationAssemblyResult Finish()
        {
            var result = document with { Entities = entities.ToArray(), Relations = relations.ToArray() };
            CheckDocumentSize(result);
            if (JsonSerializer.SerializeToUtf8Bytes(evidence, Json).Length > limits.MaxReportBytes)
                throw new GenerationException("report_too_large", "As evidências excedem o limite do relatório.", 422);
            return new(result, evidence.ToArray(), diagnostics.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private void CheckDocumentSize(MemoryPackDocument document)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(document, Json).Length > limits.MaxDocumentBytes)
            throw new GenerationException("document_too_large", "O pacote gerado excede o limite de JSON v1.", 422);
    }
    private static void Limit(string collection) => throw new GenerationException("document_limit",
        $"O resultado excede o limite de {collection} do JSON v1. Revise os candidatos antes de continuar.", 422);
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= max;
    private static bool OptionalText(string? value, int max) => value is null || value.Trim().Length <= max;
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Named<T>(string? value, out string? name) where T : struct, Enum
    {
        name = value is null ? null : Enum.GetNames<T>().FirstOrDefault(candidate =>
            string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is not null;
    }
    private static string ItemId(string kind, params string[] parts) => kind + ":" + Hash(
        JsonSerializer.Serialize(parts, Json));
    private static bool SupportedName(string name, GenerationEvidence[] citations)
    {
        if (citations.Any(citation => citation.Quote.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return true;
        // Descriptive labels may paraphrase grammar, but may not introduce an unseen name.
        var ignored = new HashSet<string>(["a", "o", "as", "os", "um", "uma", "de", "da", "do",
            "das", "dos", "com", "sem", "e", "the", "of", "with"], StringComparer.Ordinal);
        var words = Regex.Matches(Fold(name), @"[a-z0-9]+").Select(match => match.Value)
            .Where(word => !ignored.Contains(word)).ToArray();
        var sourceWords = Regex.Matches(Fold(string.Join(" ", citations.Select(citation => citation.Quote))),
            @"[a-z0-9]+").Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        return words.Length > 0 && words.All(word => sourceWords.Contains(word)
            || ((word == "mascarada" || word == "mascarado") && sourceWords.Contains("mascara")));
    }
    private static string Hash(string text) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Fold(string text)
    {
        var result = new StringBuilder();
        foreach (var character in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(char.ToLowerInvariant(character));
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
