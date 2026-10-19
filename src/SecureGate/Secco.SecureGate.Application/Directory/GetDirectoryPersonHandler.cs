using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Directory;

/// <summary>Detalha uma pessoa do diretório do tenant do token; fora do tenant responde como inexistente (ADR-0045).</summary>
public sealed class GetDirectoryPersonHandler(IDirectoryReader reader)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da claim do token.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<DirectoryPersonDto>> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var person = await reader.FindAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

		return person is null ? SecureGateErrors.Users.NotFound : DirectoryPersonDto.From(person);
	}
}
