using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.LogEntries;

/// <summary>Leitura pontual de um registro de log do banco do tenant atual.</summary>
public sealed class GetLogEntryByIdHandler(ILogEntryRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador do registro.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<LogEntryDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var logEntry = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return logEntry is null
            ? LogStreamErrors.LogEntries.NotFound
            : LogEntryDto.FromEntity(logEntry);
    }
}
