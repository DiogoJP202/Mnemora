using System.Text.Json;
using Mnemora.Application;
using Mnemora.Infrastructure;

namespace Mnemora.Tests;

public sealed class MemoryPackGenerationAssemblerTests
{
    private readonly MemoryPackGenerationAssembler assembler = new();
    private static readonly MemoryPackBook Book = new("As Máscaras do Cais", "Equipe Mnemora");
    private static readonly GenerationSource[] Sources =
    [
        new("O armário", "A mascarada carrega um mapa dobrado.\n\nLia suspeita que A mascarada levou o sino, mas não tem prova."),
        new("O mapa", "A mascarada carrega um mapa dobrado."),
        new("O acordo", "Lia e A mascarada firmam um acordo de colaboração."),
        new("A assinatura", "A mascarada diz: Meu nome é Íria.\n\nO diário relata a Queda da Ponte de Sal, ocorrida sete anos antes.")
    ];

    [Fact]
    public void Creates_exact_edition_and_neutral_outline_without_v1_extensions()
    {
        var document = Start();
        Assert.Same(Book, document.Book);
        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal(new[] { "capitulo-001", "capitulo-002", "capitulo-003", "capitulo-004" },
            document.ReadingUnits.Select(unit => unit.Slug));
        Assert.Equal("Capítulo 4", document.ReadingUnits[3].SafeLabel);
        Assert.All(document.ReadingUnits, unit =>
        {
            Assert.Equal("Chapter", unit.Type);
            Assert.Null(unit.Description);
            Assert.Null(unit.ParentSlug);
        });
        Assert.Empty(document.Entities);
        Assert.Empty(document.Relations);
        Assert.Equal("Universo fictício original da Equipe Mnemora.", document.Source!.Attribution);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("evidence", json);
        Assert.DoesNotContain("sourceHash", json);
    }

    [Fact]
    public void Identity_revealed_later_does_not_rewrite_initial_presentation_or_earlier_registry()
    {
        var first = Introduce();
        var original = first.Document.Entities.Single();
        var fourthInput = assembler.BuildInput(first.Document, Sources[3], 3);
        var fourth = assembler.Apply(first.Document, fourthInput, Extraction(
            aliases: [new(original.Slug, "Íria", [Evidence("p1", "Meu nome é Íria")])],
            facts: [new(original.Slug, "Revela o nome Íria.", "Meu nome é Íria", "Title",
                [Evidence("p1", "Meu nome é Íria")])]), first.Evidence);

        var entity = fourth.Document.Entities.Single();
        Assert.Equal(original.Name, entity.Name);
        Assert.Equal(original.ShortDescription, entity.ShortDescription);
        Assert.Equal("capitulo-001", entity.FirstKnownAtUnitSlug);
        Assert.Equal("capitulo-004", entity.Aliases.Single().RevealAtUnitSlug);
        Assert.Equal("capitulo-004", entity.Facts.Single().RevealAtUnitSlug);
        Assert.Equal("Meu nome é Íria", entity.Facts.Single().MemoryHint);
        Assert.Empty(original.Aliases);
        Assert.Empty(original.Facts);
        Assert.Equal(1, entity.Importance);
        Assert.Null(entity.ImageUrl);
        Assert.Null(entity.ChronologyIndex);

        var secondInput = assembler.BuildInput(fourth.Document, Sources[1], 1);
        var secondRegistry = secondInput.KnownEntities.Single();
        Assert.Equal("A mascarada", secondRegistry.Name);
        Assert.Empty(secondRegistry.Aliases);
        Assert.DoesNotContain("Íria", JsonSerializer.Serialize(secondInput));
        Assert.Empty(assembler.BuildInput(fourth.Document, Sources[0], 0).KnownEntities);
        Assert.All(fourth.Evidence, item => Assert.Equal(64, item.SourceHash.Length));
    }

    [Fact]
    public void Complete_book_and_prefix_only_generation_have_the_same_early_knowledge()
    {
        var completeStart = Start();
        var prefixStart = Start(Sources.Take(1).ToArray());
        var extraction = Introduction();
        var completeEarly = assembler.Apply(completeStart,
            assembler.BuildInput(completeStart, Sources[0], 0), extraction);
        var prefixEarly = assembler.Apply(prefixStart,
            assembler.BuildInput(prefixStart, Sources[0], 0), extraction);
        Assert.Equal(JsonSerializer.Serialize(prefixEarly.Document.Entities),
            JsonSerializer.Serialize(completeEarly.Document.Entities));
        var completeFinal = assembler.Apply(completeEarly.Document,
            assembler.BuildInput(completeEarly.Document, Sources[3], 3), Extraction(
                aliases: [new("entity-0001", "Íria", [Evidence("p1", "Meu nome é Íria")])]),
            completeEarly.Evidence);
        var beforeSecond = assembler.BuildInput(completeFinal.Document, Sources[1], 1).KnownEntities;
        var prefixRegistry = assembler.BuildInput(completeEarly.Document, Sources[1], 1).KnownEntities;
        Assert.Equal(JsonSerializer.Serialize(prefixRegistry), JsonSerializer.Serialize(beforeSecond));
    }

    [Fact]
    public void Old_event_and_late_relation_are_revealed_at_the_current_reading_unit()
    {
        var first = Introduce(includeLia: true);
        var third = assembler.Apply(first.Document, assembler.BuildInput(first.Document, Sources[2], 2),
            Extraction(relations: [new("entity-0002", "entity-0001", "ColaboraCom", "Firmam um acordo.",
                [Evidence("p1", "Lia e A mascarada firmam um acordo de colaboração.")])]), first.Evidence);
        Assert.Equal("capitulo-003", third.Document.Relations.Single().RevealAtUnitSlug);
        var fourth = assembler.Apply(third.Document, assembler.BuildInput(third.Document, Sources[3], 3),
            Extraction(newEntities: [new("old-event", "Event", "Queda da Ponte de Sal", null,
                [Evidence("p2", "O diário relata a Queda da Ponte de Sal, ocorrida sete anos antes.")])]),
            third.Evidence);
        var oldEvent = fourth.Document.Entities.Single(entity => entity.Type == "Event");
        Assert.Equal("capitulo-004", oldEvent.FirstKnownAtUnitSlug);
        Assert.Null(oldEvent.ChronologyIndex);
        Assert.Equal("capitulo-003", fourth.Document.Relations.Single().RevealAtUnitSlug);
    }

    [Fact]
    public void Invalid_evidence_types_and_orphan_references_are_discarded_with_diagnostics()
    {
        var document = Start();
        var input = assembler.BuildInput(document, Sources[0], 0);
        var result = assembler.Apply(document, input, Extraction(
            newEntities:
            [
                new("missing-quote", "Character", "A mascarada", null,
                    [Evidence("p1", "Uma rainha chamada Íria.")]),
                new("missing-paragraph", "Character", "A mascarada", null,
                    [Evidence("p20", "A mascarada")]),
                new("numeric-type", "1", "A mascarada", null, [Evidence("p1", "A mascarada")]),
                new("future-name", "Character", "Íria", null, [Evidence("p1", "A mascarada")])
            ],
            facts: [new("missing-quote", "Uma certeza.", null, "Background", [Evidence("p1", "A mascarada")])],
            relations: [new("missing-quote", "unseen", "AliadoDe", null, [Evidence("p1", "A mascarada")])]));
        Assert.Empty(result.Document.Entities);
        Assert.Empty(result.Document.Relations);
        Assert.Empty(result.Evidence);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Empty(document.Entities);
    }

    [Fact]
    public void Repetition_preserves_the_first_reveal_and_hint_but_adds_evidence()
    {
        var first = Introduce();
        var firstFact = assembler.Apply(first.Document, assembler.BuildInput(first.Document, Sources[1], 1),
            Extraction(facts: [new("entity-0001", "Carrega um mapa dobrado.", "mapa dobrado", "Description",
                [Evidence("p1", "A mascarada carrega um mapa dobrado.")])]), first.Evidence);
        var secondSource = new GenerationSource(Sources[3].Title, "A mascarada carrega um mapa dobrado.");
        var repeated = assembler.Apply(firstFact.Document,
            assembler.BuildInput(firstFact.Document, secondSource, 3), Extraction(
                facts: [new("entity-0001", "Carrega um mapa dobrado.", "A mascarada", "Description",
                    [Evidence("p1", "A mascarada carrega um mapa dobrado.")])]), firstFact.Evidence);
        var fact = repeated.Document.Entities.Single().Facts.Single();
        Assert.Equal("capitulo-002", fact.RevealAtUnitSlug);
        Assert.Equal("mapa dobrado", fact.MemoryHint);
        var factEvidence = repeated.Evidence.Where(item => item.JsonPath == "entities[0].facts[0]").ToArray();
        Assert.Equal(2, factEvidence.Length);
        Assert.Single(factEvidence.Select(item => item.ItemId).Distinct());
        Assert.Equal(new[] { "capitulo-002", "capitulo-004" }, factEvidence.Select(item => item.UnitSlug));
    }

    [Fact]
    public void Uncited_description_and_future_hint_are_omitted_while_literal_current_hint_is_kept()
    {
        var document = Start();
        var first = assembler.Apply(document, assembler.BuildInput(document, Sources[0], 0), Extraction(
            newEntities: [new("mask", "Character", "A mascarada", "É a rainha Íria.",
                [Evidence("p1", "A mascarada carrega um mapa dobrado.")])],
            facts:
            [
                new("mask", "Carrega um mapa.", "Íria", "Description", [Evidence("p1", "A mascarada carrega um mapa dobrado.")]),
                new("mask", "Carrega um mapa dobrado.", "mapa dobrado", "Description", [Evidence("p1", "A mascarada carrega um mapa dobrado.")])
            ]));
        var entity = first.Document.Entities.Single();
        Assert.Null(entity.ShortDescription);
        Assert.Null(entity.Facts[0].MemoryHint);
        Assert.Equal("mapa dobrado", entity.Facts[1].MemoryHint);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Contains("Descrição inicial omitida"));
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Contains("Pista de memória omitida"));
        Assert.DoesNotContain("Íria", JsonSerializer.Serialize(first.Document));
    }

    [Fact]
    public void Attributed_suspicion_is_preserved_and_does_not_become_a_confirmed_identity_or_relation()
    {
        var first = Introduce(includeLia: true);
        var current = new GenerationSource(Sources[1].Title,
            "Lia suspeita que A mascarada seja Íria. Lia acredita que A mascarada colabora com ela.");
        var result = assembler.Apply(first.Document, assembler.BuildInput(first.Document, current, 1),
            Extraction(
                aliases: [new("entity-0001", "Íria", [Evidence("p1", "Lia suspeita que A mascarada seja Íria.")])],
                facts:
                [
                    new("entity-0001", "A mascarada é Íria.", null, "Background", [Evidence("p1", "Lia suspeita que A mascarada seja Íria.")]),
                    new("entity-0001", "Lia suspeita que A mascarada seja Íria.", null, "Background", [Evidence("p1", "Lia suspeita que A mascarada seja Íria.")])
                ],
                relations: [new("entity-0002", "entity-0001", "ColaboraCom", null,
                    [Evidence("p1", "Lia acredita que A mascarada colabora com ela.")])]), first.Evidence);
        Assert.Empty(result.Document.Entities[0].Aliases);
        Assert.Equal("Lia suspeita que A mascarada seja Íria.", result.Document.Entities[0].Facts.Single().Content);
        Assert.Empty(result.Document.Relations);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("Alias retido"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("Relação retida"));
    }

    [Fact]
    public void Retrospective_source_and_future_fact_are_retained_for_review_without_claiming_semantic_proof()
    {
        var first = Introduce();
        var current = new GenerationSource(Sources[1].Title, "A mascarada é a futura rainha.");
        var result = assembler.Apply(first.Document, assembler.BuildInput(first.Document, current, 1),
            Extraction(facts: [new("entity-0001", "É a futura rainha.", null, "Background",
                [Evidence("p1", "A mascarada é a futura rainha.")])]), first.Evidence);
        Assert.Empty(result.Document.Entities.Single().Facts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("retrospectiva"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("Fato retido"));
    }

    [Fact]
    public void Ambiguous_personas_remain_separate_and_local_id_collisions_cannot_update_known_entities()
    {
        var first = Introduce();
        var current = new GenerationSource(Sources[1].Title, "Outra viajante também é chamada A mascarada.");
        var result = assembler.Apply(first.Document, assembler.BuildInput(first.Document, current, 1), Extraction(
            newEntities:
            [
                new("other-mask", "Character", "A mascarada", null, [Evidence("p1", "A mascarada")]),
                new("entity-0001", "Character", "A mascarada", null, [Evidence("p1", "A mascarada")]),
                new("duplicate", "Character", "A mascarada", null, [Evidence("p1", "A mascarada")]),
                new("duplicate", "Character", "A mascarada", null, [Evidence("p1", "A mascarada")])
            ]), first.Evidence);
        Assert.Equal(2, result.Document.Entities.Count);
        Assert.Equal("entity-0001", result.Document.Entities[0].Slug);
        Assert.Equal("entity-0002", result.Document.Entities[1].Slug);
        Assert.Equal("capitulo-001", result.Document.Entities[0].FirstKnownAtUnitSlug);
        Assert.Equal("capitulo-002", result.Document.Entities[1].FirstKnownAtUnitSlug);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("permanecem separadas"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("referência local duplicada"));
    }

    [Fact]
    public void Missing_source_adds_only_a_gap_and_limits_never_mutate_previous_document()
    {
        var document = Start();
        var blank = new GenerationSource(Sources[0].Title, " ");
        var missing = assembler.Apply(document, assembler.BuildInput(document, blank, 0), Introduction());
        Assert.Empty(missing.Document.Entities);
        Assert.Empty(missing.Evidence);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.StartsWith("Fonte ausente"));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
        var limited = new MemoryPackGenerationAssembler(new GenerationOptions { MaxDocumentBytes = bytes + 10 });
        var error = Assert.Throws<GenerationException>(() => limited.Apply(document,
            limited.BuildInput(document, Sources[0], 0), Introduction()));
        Assert.Equal("document_too_large", error.Code);
        Assert.Empty(document.Entities);
    }

    [Fact]
    public void Future_entities_cannot_be_smuggled_into_the_known_registry_or_references()
    {
        var first = Introduce();
        var fourth = assembler.Apply(first.Document, assembler.BuildInput(first.Document, Sources[3], 3),
            Extraction(newEntities: [new("event", "Event", "Queda da Ponte de Sal", null,
                [Evidence("p2", "Queda da Ponte de Sal")])]), first.Evidence);
        var input = assembler.BuildInput(fourth.Document, Sources[1], 1);
        Assert.Single(input.KnownEntities);
        var spoofed = input with { KnownEntities = input.KnownEntities.Append(
            new GenerationKnownEntity("entity-0002", "Event", "Queda da Ponte de Sal", null, [])).ToArray() };
        var result = assembler.Apply(fourth.Document, spoofed,
            Extraction(facts: [new("entity-0002", "Um evento antigo.", null, "Background",
                [Evidence("p1", "A mascarada")])]), fourth.Evidence);
        Assert.Empty(result.Document.Entities[1].Facts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("participante desconhecida"));
    }

    [Fact]
    public void Structured_candidates_cannot_inject_v1_reveal_or_image_fields()
    {
        const string json = """
            {"localId":"mask","type":"Character","name":"A mascarada","shortDescription":null,"evidence":[],"firstKnownAtUnitSlug":"capitulo-004","imageUrl":"https://example.com/queen.jpg"}
            """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GenerationNewEntity>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("entities")]
    [InlineData("aliases")]
    [InlineData("facts")]
    [InlineData("relations")]
    public void V1_collection_limits_reject_the_whole_step_without_mutating_the_input(string collection)
    {
        var first = Introduce(includeLia: true);
        var document = first.Document;
        ChapterExtraction extraction;
        var index = 3;
        if (collection == "entities")
        {
            document = document with { Entities = Enumerable.Range(1, 500).Select(number =>
                document.Entities[0] with { Slug = $"entity-{number:0000}" }).ToArray() };
            extraction = Extraction(newEntities: [new("new", "Character", "Íria", null,
                [Evidence("p1", "Meu nome é Íria")])]);
        }
        else if (collection == "aliases")
        {
            document = document with { Entities = [document.Entities[0] with
            {
                Aliases = Enumerable.Range(1, 20).Select(number =>
                    new MemoryPackAlias($"Nome {number}", "capitulo-001")).ToArray()
            }, document.Entities[1]] };
            extraction = Extraction(aliases: [new("entity-0001", "Íria", [Evidence("p1", "Meu nome é Íria")])]);
        }
        else if (collection == "facts")
        {
            document = document with { Entities = [document.Entities[0] with
            {
                Facts = Enumerable.Range(1, 100).Select(number =>
                    new MemoryPackFact($"Fato {number}", null, "Description", 1, "capitulo-001")).ToArray()
            }, document.Entities[1]] };
            extraction = Extraction(facts: [new("entity-0001", "Revela o nome Íria.", null, "Title",
                [Evidence("p1", "Meu nome é Íria")])]);
        }
        else
        {
            index = 2;
            document = document with { Relations = Enumerable.Range(1, 5_000).Select(number =>
                new MemoryPackRelation("entity-0001", "entity-0002", $"Tipo{number}", null,
                    "capitulo-001")).ToArray() };
            extraction = Extraction(relations: [new("entity-0001", "entity-0002", "ColaboraCom", null,
                [Evidence("p1", "Lia e A mascarada firmam um acordo de colaboração.")])]);
        }
        var before = JsonSerializer.Serialize(document);
        var error = Assert.Throws<GenerationException>(() => assembler.Apply(document,
            assembler.BuildInput(document, Sources[index], index), extraction, first.Evidence));
        Assert.Equal("document_limit", error.Code);
        Assert.Equal(before, JsonSerializer.Serialize(document));
    }

    [Fact]
    public void Report_limit_prevents_committing_untraceable_content()
    {
        var document = Start();
        var limited = new MemoryPackGenerationAssembler(new GenerationOptions { MaxReportBytes = 50 });
        var error = Assert.Throws<GenerationException>(() => limited.Apply(document,
            limited.BuildInput(document, Sources[0], 0), Introduction()));
        Assert.Equal("report_too_large", error.Code);
        Assert.Empty(document.Entities);
    }

    [Fact]
    public void Descriptive_name_can_reuse_known_terms_without_inventing_a_future_name()
    {
        var document = Start();
        var source = new GenerationSource(Sources[0].Title, "Uma viajante com máscara cinza chega ao cais.");
        var result = assembler.Apply(document, assembler.BuildInput(document, source, 0), Extraction(
            newEntities: [new("visitor", "Character", "Viajante de máscara", null,
                [Evidence("p1", "Uma viajante com máscara cinza chega ao cais.")])]));
        Assert.Equal("Viajante de máscara", result.Document.Entities.Single().Name);
    }

    private MemoryPackDocument Start(GenerationSource[]? sources = null) => assembler.Create(Book,
        new(sources ?? Sources, "Universo fictício original da Equipe Mnemora.", EditionConfirmed: true,
            IndexComplete: true));
    private GenerationAssemblyResult Introduce(bool includeLia = false)
    {
        var document = Start();
        return assembler.Apply(document, assembler.BuildInput(document, Sources[0], 0), Introduction(includeLia));
    }
    private static ChapterExtraction Introduction(bool includeLia = false)
    {
        var entities = new List<GenerationNewEntity>
        {
            new("mask", "Character", "A mascarada", "A mascarada carrega um mapa dobrado.",
                [Evidence("p1", "A mascarada carrega um mapa dobrado.")])
        };
        if (includeLia) entities.Add(new("lia", "Character", "Lia", null,
            [Evidence("p2", "Lia suspeita que A mascarada levou o sino, mas não tem prova.")]));
        return Extraction(newEntities: entities);
    }
    private static GenerationEvidence Evidence(string paragraphId, string quote) =>
        new(paragraphId, quote, "O trecho informa este elemento na unidade corrente.");
    private static ChapterExtraction Extraction(
        IReadOnlyList<GenerationNewEntity>? newEntities = null,
        IReadOnlyList<GenerationFact>? facts = null,
        IReadOnlyList<GenerationAlias>? aliases = null,
        IReadOnlyList<GenerationRelation>? relations = null) =>
        new(newEntities ?? [], facts ?? [], aliases ?? [], relations ?? [], []);
}
