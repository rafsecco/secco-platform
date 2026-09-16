namespace Secco.SecureGate.Application.Users;

/// <summary>
/// A instalação nunca fica sem operador ativo: reativar ou reatribuir exige token de operador, então
/// zerar os operadores só se desfaz com acesso direto ao banco.
/// </summary>
/// <remarks>
/// Duas operações simultâneas sobre os dois últimos operadores podem, cada uma, ver a outra ainda
/// ativa. Risco residual aceito: exige dois admins agindo ao mesmo tempo sobre os últimos operadores.
/// </remarks>
internal static class OperatorGuard
{
	/// <summary>Indica se tirar o usuário da condição de operador ativo deixaria a instalação sem nenhum.</summary>
	/// <param name="directory">Diretório de usuários.</param>
	/// <param name="userId">Usuário alvo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<bool> WouldLeaveNoActiveOperatorAsync(
		IUserDirectory directory, Guid userId, CancellationToken cancellationToken) =>
		await directory.HasRoleAsync(SecureGatePlatform.TenantId, userId, SecureGatePlatform.OperatorRole, cancellationToken)
			.ConfigureAwait(false)
		&& await directory.CountActiveOperatorsAsync(userId, cancellationToken).ConfigureAwait(false) == 0;
}
