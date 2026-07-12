using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.ApiCalls;

/// <summary>Leitura pontual de uma chamada de API do banco do tenant atual.</summary>
public sealed class GetApiCallLogByIdHandler(IApiCallLogRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador do registro.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<ApiCallLogDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var apiCallLog = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return apiCallLog is null
            ? LogStreamErrors.ApiCalls.NotFound
            : ApiCallLogDto.FromEntity(apiCallLog);
    }
}
