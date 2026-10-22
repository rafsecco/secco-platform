namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>Uma escrita feita com a autoridade <c>securegate:tenant-manage</c> (ADR-0052/0054).</summary>
/// <param name="TenantId">Tenant administrado — o da rota, que é o do token.</param>
/// <param name="ClientId">Client (máquina) que agiu. Sem ele não há registro.</param>
/// <param name="Operation">Nome estável da operação (o <c>operationId</c> da rota).</param>
/// <param name="TargetUserId">Pessoa alvo, quando a rota tem uma.</param>
public sealed record TenantManageAuditRecord(Guid TenantId, string? ClientId, string Operation, Guid? TargetUserId);

/// <summary>
/// Porta da trilha da gestão do próprio tenant por máquina (ADR-0054): grava no banco do SecureGate, de forma
/// síncrona e fail-closed — a escrita só acontece DEPOIS de o registro ser confirmado. Não depende do LogStream.
/// </summary>
public interface ITenantManageAuditor
{
	/// <summary>Grava o registro. Devolve <c>false</c> em qualquer falha, sem lançar.</summary>
	/// <param name="record">O que registrar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> RecordAsync(TenantManageAuditRecord record, CancellationToken cancellationToken = default);
}
