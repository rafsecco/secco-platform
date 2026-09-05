using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.Audit;

/// <summary>Leitura pontual de uma entrada de auditoria do banco do tenant atual.</summary>
public sealed class GetAuditEntryByIdHandler(IAuditEntryRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Identificador da entrada.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<AuditEntryDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var auditEntry = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		return auditEntry is null
			? LogStreamErrors.AuditEntries.NotFound
			: AuditEntryDto.FromEntity(auditEntry);
	}
}
