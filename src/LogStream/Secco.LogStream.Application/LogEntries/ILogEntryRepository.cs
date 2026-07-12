using Secco.LogStream.Domain.LogEntries;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Application.LogEntries;

/// <summary>Porta de persistência de registros de log — sempre no banco do tenant atual (ADR-0005).</summary>
public interface ILogEntryRepository
{
    /// <summary>Persiste um registro de log.</summary>
    /// <param name="logEntry">Registro a persistir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(LogEntry logEntry, CancellationToken cancellationToken = default);

    /// <summary>Busca um registro pelo identificador.</summary>
    /// <param name="id">Identificador do registro.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<LogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Busca paginada com os filtros informados, ordenada do mais recente ao mais antigo.</summary>
    /// <param name="criteria">Filtros e paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PagedResult<LogEntry>> SearchAsync(LogEntrySearchCriteria criteria, CancellationToken cancellationToken = default);
}
