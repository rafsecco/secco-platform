namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>Uma escrita feita com a autoridade de admin de tenant (ADR-0044).</summary>
/// <param name="UserId">Quem agiu — o <c>sub</c> do token.</param>
/// <param name="UserName">Snapshot legível de quem agiu (claim <c>name</c>), se houver.</param>
/// <param name="TenantId">
/// Onde a entrada é gravada: o tenant administrado quando ele tem banco do LogStream no catálogo; senão, o
/// tenant dono de quem agiu (ADR-0047).
/// </param>
/// <param name="ClientId">Aplicação pela qual a pessoa agiu (o <c>act</c> do pedido).</param>
/// <param name="Operation">Nome estável da operação (o <c>operationId</c> da rota).</param>
/// <param name="TargetUserId">Pessoa alvo, quando a rota tem uma.</param>
/// <param name="AdministeredTenantId">Tenant sobre o qual a pessoa agiu; nulo na criação, quando ele ainda não existe.</param>
public sealed record TenantAdminAuditRecord(
	Guid UserId,
	string? UserName,
	Guid TenantId,
	string? ClientId,
	string Operation,
	Guid? TargetUserId,
	Guid? AdministeredTenantId = null);

/// <summary>
/// Porta de auditoria das escritas por admin de tenant (ADR-0044). Síncrona e fail-closed: a escrita
/// só acontece DEPOIS de o registro ser aceito.
/// </summary>
public interface ITenantAdminAuditor
{
	/// <summary>
	/// Indica se há identidade de auditoria configurada. Sem ela a autoridade de admin de tenant fica
	/// desligada: o escopo não sai no login e as rotas a recusam.
	/// </summary>
	bool IsConfigured { get; }

	/// <summary>Grava o registro. Devolve <c>false</c> em qualquer falha, sem lançar.</summary>
	/// <param name="record">O que registrar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> RecordAsync(TenantAdminAuditRecord record, CancellationToken cancellationToken = default);
}
