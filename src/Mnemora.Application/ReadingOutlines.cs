namespace Mnemora.Application;

public sealed record ExternalReadingOutlineItem(
    string Title, int Level, int? Page);

public sealed record ExternalReadingOutline(
    string Provider,
    string ExternalId,
    string SourceUrl,
    string Attribution,
    IReadOnlyList<ExternalReadingOutlineItem> Items);

public sealed class ReadingOutlineLimitExceededException(int limit)
    : Exception($"O sumário excede o limite de {limit} unidades. Revise o índice manualmente para preservar o livro completo.");

public interface IReadingOutlineProvider
{
    Task<ExternalReadingOutline?> GetAsync(
        string provider, string externalId, CancellationToken cancellationToken);
}
