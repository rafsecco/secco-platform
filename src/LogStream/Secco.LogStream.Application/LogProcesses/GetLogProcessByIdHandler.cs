using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.LogProcesses;

/// <summary>Leitura pontual de um processo (com status agregado) do banco do tenant atual.</summary>
public sealed class GetLogProcessByIdHandler(ILogProcessRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador do processo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<LogProcessDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var summary = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return summary is null
            ? LogStreamErrors.LogProcesses.NotFound
            : LogProcessDto.FromSummary(summary);
    }
}
