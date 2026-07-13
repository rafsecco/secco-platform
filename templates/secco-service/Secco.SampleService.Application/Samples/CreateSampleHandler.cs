using Secco.SampleService.Domain.Samples;
using Secco.SharedKernel.Results;

namespace Secco.SampleService.Application.Samples;

/// <summary>Comando de criação de um sample.</summary>
/// <param name="Name">Nome. Obrigatório.</param>
/// <param name="Description">Descrição livre, quando houver.</param>
public sealed record CreateSampleCommand(string? Name, string? Description = null);

/// <summary>
/// Caso de uso de exemplo: valida limites (ADR-0020), cria a entidade e persiste —
/// erros de negócio fluem por <see cref="Result{T}"/>, nunca por exceção (ADR-0004).
/// </summary>
public sealed class CreateSampleHandler(ISampleRepository repository, SampleServiceOptions options)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de criação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<SampleDto>> HandleAsync(CreateSampleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return SampleServiceErrors.Samples.NameRequired;
        }

        if (command.Name.Length > options.MaxNameLength)
        {
            return SampleServiceErrors.Samples.NameTooLong(options.MaxNameLength);
        }

        var description = command.Description?.Length > options.MaxDescriptionLength
            ? command.Description[..options.MaxDescriptionLength]
            : command.Description;

        var sample = new Sample(command.Name, description);

        await repository.AddAsync(sample, cancellationToken).ConfigureAwait(false);

        return SampleDto.FromEntity(sample);
    }
}
