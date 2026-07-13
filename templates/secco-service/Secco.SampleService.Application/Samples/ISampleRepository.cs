using Secco.SampleService.Domain.Samples;
using Secco.SharedKernel.Pagination;

namespace Secco.SampleService.Application.Samples;

/// <summary>Filtros da busca de samples. Todos opcionais; combinados com AND.</summary>
/// <param name="NameContains">Trecho contido no nome.</param>
/// <param name="Page">Paginação (1-based).</param>
public sealed record SampleSearchCriteria(string? NameContains = null, PageRequest? Page = null)
{
    /// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
    public PageRequest EffectivePage => Page ?? PageRequest.Default;
}

/// <summary>Porta de persistência de samples — sempre no banco do tenant atual (ADR-0005).</summary>
public interface ISampleRepository
{
    /// <summary>Persiste um sample.</summary>
    /// <param name="sample">Sample a persistir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(Sample sample, CancellationToken cancellationToken = default);

    /// <summary>Busca um sample pelo identificador.</summary>
    /// <param name="id">Identificador do sample.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<Sample?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Busca paginada, mais recentes primeiro.</summary>
    /// <param name="criteria">Filtros e paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PagedResult<Sample>> SearchAsync(SampleSearchCriteria criteria, CancellationToken cancellationToken = default);
}
