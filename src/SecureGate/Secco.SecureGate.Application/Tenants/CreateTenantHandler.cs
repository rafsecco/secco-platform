using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>Comando de criação de tenant.</summary>
/// <param name="Name">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto único (kebab-case). Obrigatório.</param>
/// <param name="CreatedBy">
/// Pessoa que cria pela autoridade de admin de tenant (ADR-0047) — o tenant nasce administrado por ela. Nulo
/// quando quem cria é o operador. Vem da claim do chamador, nunca do corpo.
/// </param>
public sealed record CreateTenantCommand(string? Name, string? Slug, Guid? CreatedBy = null);

/// <summary>
/// Cria um tenant no catálogo da plataforma (nasce ativo, sem bancos cadastrados). Criado por pessoa (ADR-0047),
/// o tenant e a concessão de admin dela sobre ele nascem na mesma transação, dentro do limite por pessoa.
/// </summary>
public sealed class CreateTenantHandler(
	ITenantRepository repository,
	ITenantAdminGrantRepository grants,
	ITenantAccessScope accessScope,
	TenantAdminOptions options)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<TenantDto>> HandleAsync(
		CreateTenantCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Name))
		{
			return SecureGateErrors.Tenants.NameRequired;
		}

		if (command.Name.Length > Tenant.NameMaxLength)
		{
			return SecureGateErrors.Tenants.NameTooLong;
		}

		var slug = command.Slug?.Trim().ToLowerInvariant() ?? string.Empty;

		if (!TenantInputRules.IsValidSlug(slug, Tenant.SlugMaxLength))
		{
			return SecureGateErrors.Tenants.SlugInvalid;
		}

		if (await repository.SlugExistsAsync(slug, cancellationToken).ConfigureAwait(false))
		{
			return SecureGateErrors.Tenants.SlugAlreadyExists;
		}

		// Limite antes de qualquer escrita: a contagem é das concessões que nasceram de criação (ADR-0047).
		// Duas criações simultâneas podem passar do limite por uma — contenção, não cota contábil.
		if (command.CreatedBy is { } creator
			&& await grants.CountCreatedTenantsAsync(creator, cancellationToken).ConfigureAwait(false)
				>= options.EffectiveMaxCreatedTenantsPerPerson)
		{
			return SecureGateErrors.TenantAdmins.CreationLimitReached;
		}

		var tenant = new Tenant(command.Name.Trim(), slug);

		await repository.AddAsync(tenant, cancellationToken).ConfigureAwait(false);

		if (command.CreatedBy is { } createdBy)
		{
			// Mesma transação: a concessão é entidade do tenant novo, gravada no escopo dele (ADR-0041), e o
			// SaveChanges dela leva o tenant junto — nunca um tenant sem quem o criou administrá-lo
			using (accessScope.EnterTenant(tenant.Id))
			{
				await grants.AddAsync(
					new TenantAdminGrant(createdBy, tenant.Id, createdBy.ToString(), DateTimeOffset.UtcNow, createdTenant: true),
					cancellationToken).ConfigureAwait(false);
			}
		}
		else
		{
			await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return TenantDto.FromEntity(tenant);
	}
}
