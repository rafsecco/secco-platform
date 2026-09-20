using System.Net.Mail;
using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Comando de criação de usuário (provisionamento por administrador).</summary>
/// <param name="TenantId">Tenant ao qual o usuário pertence.</param>
/// <param name="Email">E-mail (também o username). Obrigatório.</param>
/// <param name="LocalLogin">
/// <c>true</c> (padrão) cria a conta sem senha e envia o convite; <c>false</c> cria conta que entra
/// só pelo diretório corporativo (ADR-0026) — sem senha, sem convite e sem recuperação.
/// </param>
/// <param name="Roles">Roles a atribuir no tenant (opcional).</param>
public sealed record CreateUserCommand(Guid TenantId, string? Email, bool LocalLogin, IReadOnlyList<string>? Roles);

/// <summary>
/// Cria um usuário no tenant. Valida e-mail e a existência do tenant e dos roles ANTES de acionar
/// o Identity (ADR-0020: nada não confiável chega ao provedor sem validação de formato).
/// </summary>
/// <remarks>
/// <b>O admin não define senha (ADR-0033):</b> a conta nasce sem hash e a pessoa escolhe a
/// credencial pelo convite. Antes disto existia um instante — normalmente longo — em que a senha
/// era conhecida por quem criou a conta e trafegava por WhatsApp ou e-mail escrito à mão.
/// </remarks>
public sealed class CreateUserHandler(
	IRoleRepository roleRepository,
	IUserDirectory userDirectory,
	Credentials.ICredentialTokens credentialTokens,
	Credentials.InviteUserHandler inviteHandler)
{
	/// <summary>Tamanho máximo aceito para o e-mail.</summary>
	private const int EmailMaxLength = 256;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<UserDto>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var email = command.Email?.Trim() ?? string.Empty;

		if (email.Length is 0 or > EmailMaxLength || !MailAddress.TryCreate(email, out _))
		{
			return Result.Failure<UserDto>(SecureGateErrors.Users.EmailInvalid);
		}

		if (!await roleRepository.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure<UserDto>(SecureGateErrors.Tenants.NotFound);
		}

		var roles = command.Roles ?? [];

		foreach (var role in roles)
		{
			if (!RoleInputRules.IsAssignableToUsers(role))
			{
				return Result.Failure<UserDto>(SecureGateErrors.Users.RoleNotAssignable);
			}

			if (!await roleRepository.RoleExistsAsync(command.TenantId, role, cancellationToken).ConfigureAwait(false))
			{
				return Result.Failure<UserDto>(SecureGateErrors.Users.RoleNotFound);
			}
		}

		var created = await userDirectory
			.CreateAsync(new CreateUserData(command.TenantId, email, command.LocalLogin, roles), cancellationToken)
			.ConfigureAwait(false);

		if (created.IsFailure || !command.LocalLogin)
		{
			return created;
		}

		// A conta existe e está sem senha: o convite é o único caminho até a credencial.
		if (await credentialTokens.FindAsync(created.Value.Id, cancellationToken).ConfigureAwait(false) is { } account)
		{
			await inviteHandler.SendAsync(account, cancellationToken).ConfigureAwait(false);
		}

		return created;
	}
}
