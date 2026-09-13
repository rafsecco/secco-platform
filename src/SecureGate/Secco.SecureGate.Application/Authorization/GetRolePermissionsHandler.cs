using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Authorization;

/// <summary>
/// Resolução <c>(tenant, role) → permissões</c> servida aos produtos (ADR-0021) — o
/// caminho quente do <c>IPermissionResolver</c> remoto do SDK. Role desconhecido responde
/// lista VAZIA (não 404): para autorização, role inexistente e role sem permissões são
/// equivalentes — e a resposta não revela o modelo de roles do tenant (ADR-0020).
/// </summary>
public sealed class GetRolePermissionsHandler(IRoleRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant do contexto do produto chamador.</param>
	/// <param name="roleName">Nome do role (claim curta <c>role</c> do token).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<string>>> HandleAsync(
		Guid tenantId,
		string? roleName,
		CancellationToken cancellationToken = default)
	{
		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return SecureGateErrors.Roles.NameInvalid;
		}

		// ADR-0024: o operador de plataforma recebe o read-set em QUALQUER tenant — a
		// capacidade cross-tenant é decidida aqui (IAM), sem o produto/SDK saberem disso.
		// Sem contexto de usuário, o casamento é só por nome — o que impede este caso especial
		// de valer para um role de cliente é a reserva do nome na gestão (RoleInputRules.
		// IsReservedName): esse role nunca pode ser criado num tenant de cliente (ADR-0020).
		if (string.Equals(name, SecureGatePlatform.OperatorRole, StringComparison.Ordinal))
		{
			return Result.Success(SecureGatePlatform.OperatorReadPermissions);
		}

		// ADR-0031: o leitor elevado recebe um read-set MENOR que o do operador (sem a trilha de
		// auditoria). O alcance cross-tenant não vem daqui: vem de o token elevado não carregar
		// tenant_id. Um token comum com este papel por colisão continuaria preso ao próprio tenant
		// pela regra de conflito da ADR-0005 — e o nome é reservado, então a colisão nem nasce.
		if (string.Equals(name, SecureGatePlatform.ElevatedLogReaderRole, StringComparison.Ordinal))
		{
			return Result.Success(SecureGatePlatform.ElevatedLogReaderPermissions);
		}

		// ADR-0031, emenda de 2026-09-13: a ÚNICA escrita cross-tenant da plataforma, restrita a
		// gravar auditoria. Nenhuma leitura. Qualquer outra escrita cross-tenant exige ADR própria
		// (ADR-0024).
		if (string.Equals(name, SecureGatePlatform.AuditorRole, StringComparison.Ordinal))
		{
			return Result.Success(SecureGatePlatform.AuditorPermissions);
		}

		var permissions = await repository.GetPermissionsAsync(tenantId, name, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success(permissions ?? []);
	}
}
