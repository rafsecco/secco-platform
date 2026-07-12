using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Application.LogProcesses;

/// <summary>
/// Read model do processo com o agregado computado na consulta (SQL): o status deriva
/// do pior nível entre os details via <see cref="ProcessStatusRule"/> — nunca carregando
/// os details em memória.
/// </summary>
/// <param name="Id">Identificador do processo.</param>
/// <param name="Name">Nome do processo.</param>
/// <param name="ExternalReference">Referência externa do chamador.</param>
/// <param name="CorrelationId">Correlation id de origem.</param>
/// <param name="CreatedAt">Momento da criação.</param>
/// <param name="DetailCount">Quantidade de details do processo.</param>
/// <param name="MaxDetailLevel">Pior nível entre os details; nulo sem details.</param>
public sealed record LogProcessSummary(
    Guid Id,
    string Name,
    string? ExternalReference,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt,
    int DetailCount,
    LogEntryLevel? MaxDetailLevel);

/// <summary>Filtros da busca de processos. Todos opcionais; combinados com AND.</summary>
/// <param name="From">Criados a partir deste momento (inclusive).</param>
/// <param name="To">Criados até este momento (inclusive).</param>
/// <param name="NameContains">Trecho contido no nome.</param>
/// <param name="Status">Status agregado exato.</param>
/// <param name="CorrelationId">Correlation id exato.</param>
/// <param name="Page">Paginação (1-based).</param>
public sealed record LogProcessSearchCriteria(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? NameContains = null,
    ProcessStatus? Status = null,
    Guid? CorrelationId = null,
    PageRequest? Page = null)
{
    /// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
    public PageRequest EffectivePage => Page ?? PageRequest.Default;
}

/// <summary>Porta de persistência/consulta de processos — sempre no banco do tenant atual (ADR-0005).</summary>
public interface ILogProcessRepository
{
    /// <summary>Persiste um processo.</summary>
    /// <param name="logProcess">Processo a persistir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(LogProcess logProcess, CancellationToken cancellationToken = default);

    /// <summary>Persiste um detail.</summary>
    /// <param name="detail">Detail a persistir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddDetailAsync(LogProcessDetail detail, CancellationToken cancellationToken = default);

    /// <summary>Indica se o processo existe no banco do tenant atual.</summary>
    /// <param name="id">Identificador do processo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Busca um processo (com agregados) pelo identificador.</summary>
    /// <param name="id">Identificador do processo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<LogProcessSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Busca paginada de processos (com agregados), mais recentes primeiro.</summary>
    /// <param name="criteria">Filtros e paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PagedResult<LogProcessSummary>> SearchAsync(LogProcessSearchCriteria criteria, CancellationToken cancellationToken = default);

    /// <summary>Busca paginada dos details de um processo, mais recentes primeiro.</summary>
    /// <param name="logProcessId">Processo pai.</param>
    /// <param name="page">Paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PagedResult<LogProcessDetail>> GetDetailsAsync(Guid logProcessId, PageRequest page, CancellationToken cancellationToken = default);
}
