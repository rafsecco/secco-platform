using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Desfecho de criar a conta de uma pessoa do diretório.</summary>
public enum CreateDirectoryAccountOutcome
{
	/// <summary>Conta criada, vinculada ao diretório e com o vínculo de dono.</summary>
	Created,

	/// <summary>O e-mail já pertence a outra conta da instalação; nada foi criado.</summary>
	EmailInUse,
}

/// <summary>Tenant em que o primeiro login de uma pessoa do diretório pode criar conta (ADR-0055).</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="GroupId">Grupo de provisionamento do tenant.</param>
/// <param name="RoleName">Perfil a que o grupo está mapeado (o mapeamento para perfil reservado nunca vale).</param>
/// <param name="AllowedEmailDomains">Domínios de e-mail permitidos da federação.</param>
public sealed record ProvisioningTarget(
	Guid TenantId, Guid GroupId, string RoleName, IReadOnlyList<string> AllowedEmailDomains);

/// <summary>
/// Porta de persistência do provisionamento pelo diretório (ADR-0055, entrega B): criar, adotar, desativar e
/// reativar contas. Os métodos de conta rodam no escopo do tenant que o chamador já entrou (ADR-0041).
/// </summary>
public interface IDirectoryAccountStore
{
	/// <summary>Quantas contas geridas pelo diretório o tenant tem — o denominador do freio de desativação.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<int> CountManagedAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Indica se o e-mail já pertence a alguma conta da instalação, de QUALQUER tenant: o e-mail é único global
	/// (ADR-0022), e a colisão pode ser com a conta de outro tenant, invisível ao escopo corrente.
	/// </summary>
	/// <param name="email">E-mail já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken = default);

	/// <summary>
	/// Cria a conta DONA do tenant: sem senha, sem convite, e-mail confirmado, login local desligado, vinculada ao
	/// diretório por <c>{tid}:{oid}</c>, com o vínculo de dono e gerida pelo diretório (ADR-0040/0055). Tudo ou nada.
	/// </summary>
	/// <param name="tenantId">Tenant dono.</param>
	/// <param name="directoryId">Directory id da federação.</param>
	/// <param name="userId">Id da conta, já escolhido (a trilha o registra antes da criação).</param>
	/// <param name="objectId">Object id da pessoa no diretório.</param>
	/// <param name="draft">E-mail e nome já validados.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<CreateDirectoryAccountOutcome> CreateAsync(
		Guid tenantId,
		Guid directoryId,
		Guid userId,
		Guid objectId,
		DirectoryAccountDraft draft,
		CancellationToken cancellationToken = default);

	/// <summary>Marca a conta (dona do tenant) como gerida pelo diretório (adoção). Idempotente.</summary>
	/// <param name="tenantId">Tenant dono.</param>
	/// <param name="userId">Conta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se a conta não é dona do tenant.</returns>
	Task<bool> AdoptAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Desativa a conta (o mesmo lockout da desativação manual) gravando a origem <see cref="DeactivationOrigin.Directory"/>.
	/// A revogação das sessões é de quem chama.
	/// </summary>
	/// <param name="tenantId">Tenant dono.</param>
	/// <param name="userId">Conta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se a conta não é dona do tenant.</returns>
	Task<bool> DeactivateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reativa a conta e limpa a origem — SÓ se ela foi desativada pelo diretório: desativação manual nunca é
	/// desfeita por aqui, mesmo que quem chama peça.
	/// </summary>
	/// <param name="tenantId">Tenant dono.</param>
	/// <param name="userId">Conta.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se a conta não é dona do tenant ou a origem não é <c>Directory</c>.</returns>
	Task<bool> ReactivateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Tenants onde o primeiro login de uma pessoa deste diretório pode criar conta: tenant ativo (nunca o de
	/// plataforma), federação habilitada com este <c>tid</c> e grupo de provisionamento marcado. Ordem
	/// determinística: criação do tenant, depois id. Atravessa tenants por natureza (modo plataforma).
	/// </summary>
	/// <param name="directoryId">Directory id (<c>tid</c>) do login.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<ProvisioningTarget>> ListProvisioningTargetsAsync(
		Guid directoryId, CancellationToken cancellationToken = default);
}
