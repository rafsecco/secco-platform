using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Elevation;

/// <summary>
/// Confere que o usuário existe E pertence ao tenant da rota (ADR-0020/0031): a rota
/// <c>/api/v1/tenants/{tenantId}/users/{userId}/elevation</c> nunca pode revelar, pela
/// diferença de resposta, que um <c>userId</c> pertence a OUTRO tenant — usuário de outro
/// tenant responde exatamente como usuário inexistente.
/// </summary>
internal static class ElevationUserGuard
{
	/// <summary>Verifica a posse. Usado por concessão, leitura e revogação — mesma regra nos três.</summary>
	/// <param name="userDirectory">Diretório de usuários (ADR-0002 — porta já existente, não modificada).</param>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário da rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static Task<bool> BelongsToTenantAsync(
		IUserDirectory userDirectory, Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
		// Argumentos nomeados de propósito: são dois Guid, e uma inversão compilaria em silêncio.
		userDirectory.BelongsToTenantAsync(tenantId: tenantId, userId: userId, cancellationToken);
}
