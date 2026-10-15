using System.Net.Mail;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Members;

/// <summary>
/// Inclui uma pessoa que já tem conta (por e-mail) como membro do tenant (ADR-0040). Criação com chave
/// natural única: a duplicata responde <c>409</c> (ADR-0034), nunca cria um segundo vínculo.
/// </summary>
public sealed class AddTenantMemberHandler(
	ITenantRepository tenants,
	IUserDirectory userDirectory,
	ITenantMembershipRepository memberships)
{
	/// <summary>Tamanho máximo aceito para o e-mail (o mesmo do <c>CreateUserHandler</c>).</summary>
	private const int EmailMaxLength = 256;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="email">E-mail da pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<TenantMemberDto>> HandleAsync(
		Guid tenantId, string? email, CancellationToken cancellationToken = default)
	{
		var trimmed = email?.Trim() ?? string.Empty;

		if (trimmed.Length is 0 or > EmailMaxLength || !MailAddress.TryCreate(trimmed, out _))
		{
			return Result.Failure<TenantMemberDto>(SecureGateErrors.Members.EmailInvalid);
		}

		// Tenant inativo aceita: é como o admin prepara a reativação
		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
		{
			return Result.Failure<TenantMemberDto>(SecureGateErrors.Tenants.NotFound);
		}

		var person = await userDirectory.FindPersonByEmailAsync(trimmed, cancellationToken).ConfigureAwait(false);

		if (person is null)
		{
			return Result.Failure<TenantMemberDto>(SecureGateErrors.Members.UserNotFound);
		}

		if (MemberRules.IsForeignToPlatform(tenantId, person.OwnerTenantId))
		{
			return Result.Failure<TenantMemberDto>(SecureGateErrors.Members.PlatformTenantForeignUser);
		}

		// O dono já é membro pela coluna, mesmo que a linha da tabela não exista
		if (person.OwnerTenantId == tenantId)
		{
			return Result.Failure<TenantMemberDto>(SecureGateErrors.Members.AlreadyMember);
		}

		var membership = new TenantMembership(person.Id, tenantId, DateTimeOffset.UtcNow);

		if (!await memberships.TryAddAsync(membership, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure<TenantMemberDto>(SecureGateErrors.Members.AlreadyMember);
		}

		return new TenantMemberDto(
			person.Id, person.Email, person.DisplayName, person.OwnerTenantId, membership.IsActive, membership.CreatedAt);
	}
}
