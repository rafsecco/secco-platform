using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SharedKernel.Pagination;

namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>
/// Persistência da trilha de autoridade guardada no SecureGate (ADR-0054). Só acrescenta e lê; a única
/// exclusão é a da retenção opt-in, por instante do servidor.
/// </summary>
public interface IAuthorityAuditRepository
{
	/// <summary>Grava a entrada. O chamador já está no escopo do tenant dela (ADR-0041).</summary>
	/// <param name="entry">Entrada a gravar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(AuthorityAuditEntry entry, CancellationToken cancellationToken = default);

	/// <summary>Lista, do mais recente para o mais antigo, as entradas do tenant do escopo corrente.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="page">Página pedida.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PagedResult<AuthorityAuditEntryDto>> ListByTenantAsync(
		Guid tenantId, PageRequest page, CancellationToken cancellationToken = default);

	/// <summary>
	/// Apaga, em um lote, as entradas gravadas antes do corte (instante do servidor), de qualquer tenant.
	/// Idempotente: sem vencidos, apaga zero.
	/// </summary>
	/// <param name="cutoff">Corte: o que foi gravado antes dele sai.</param>
	/// <param name="batchSize">Tamanho máximo do lote.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Quantas entradas o lote apagou.</returns>
	Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default);
}
