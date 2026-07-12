using Secco.LogStream.Domain.LogEntries;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Application.LogEntries;

/// <summary>Filtros da busca de registros de log. Todos opcionais; combinados com AND.</summary>
/// <param name="From">Ocorridos a partir deste momento (inclusive).</param>
/// <param name="To">Ocorridos até este momento (inclusive).</param>
/// <param name="Level">Severidade exata.</param>
/// <param name="MessageContains">Trecho contido na mensagem (busca por substring; full-text chega na fase 4.7).</param>
/// <param name="CorrelationId">Correlation id exato.</param>
/// <param name="Page">Paginação (1-based, normalizada pelo <see cref="PageRequest"/>).</param>
public sealed record LogEntrySearchCriteria(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    LogEntryLevel? Level = null,
    string? MessageContains = null,
    Guid? CorrelationId = null,
    PageRequest? Page = null)
{
    /// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
    public PageRequest EffectivePage => Page ?? PageRequest.Default;
}
