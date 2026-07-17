using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Samples;

/// <summary>Leitura pontual de um sample do banco do tenant atual.</summary>
public sealed class GetSampleByIdHandler(ISampleRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador do sample.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<SampleDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sample = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return sample is null
            ? NotificationHubErrors.Samples.NotFound
            : SampleDto.FromEntity(sample);
    }
}
