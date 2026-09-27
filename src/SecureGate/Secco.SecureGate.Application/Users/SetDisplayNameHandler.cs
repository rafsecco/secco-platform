using Secco.SecureGate.Application.Credentials;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Comando de alteração do nome de exibição (#30).</summary>
/// <param name="TenantId">Tenant esperado (o do próprio dono, no caminho self-service).</param>
/// <param name="UserId">Usuário alvo.</param>
/// <param name="DisplayName">Valor cru, ainda não normalizado.</param>
public sealed record SetDisplayNameCommand(Guid TenantId, Guid UserId, string? DisplayName);

/// <summary>
/// Altera o nome de exibição, pelo próprio dono (conta) ou por um administrador (API). Um handler
/// só para os dois caminhos: a diferença entre eles é só QUEM pode chamar, decidida na borda
/// (página vs. escopo <c>securegate:admin</c>) — a regra de negócio é a mesma.
/// </summary>
/// <remarks>
/// Não revoga sessão nem exige senha atual: ao contrário de e-mail, senha e segundo fator, nome de
/// exibição não abre nem fecha acesso — é rótulo. Ainda assim entra na auditoria (best-effort,
/// ADR-0033), porque é a única defesa contra um admin trocando o nome de alguém sem deixar rastro.
/// </remarks>
public sealed class SetDisplayNameHandler(IUserDirectory userDirectory, ICredentialAuditor auditor)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(SetDisplayNameCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var normalized = DisplayNameRules.Normalize(command.DisplayName);

		if (normalized is not null && !DisplayNameRules.IsValid(normalized))
		{
			return Result.Failure(SecureGateErrors.Users.DisplayNameInvalid);
		}

		var found = await userDirectory
			.SetDisplayNameAsync(command.TenantId, command.UserId, normalized, cancellationToken)
			.ConfigureAwait(false);

		if (!found)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		var account = await userDirectory.GetAsync(command.TenantId, command.UserId, cancellationToken).ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.DisplayNameChanged, command.UserId, command.TenantId, account?.Email, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
