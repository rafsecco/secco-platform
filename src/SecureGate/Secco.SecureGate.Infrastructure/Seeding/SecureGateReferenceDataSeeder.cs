using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Seed de REFERÊNCIA (ADR-0019): os scopes da plataforma e a estrutura do OPERADOR
/// (ADR-0023). Scopes: um por produto (Fase 6.2, resource = audience validada pelo
/// <c>AddSeccoAuthentication()</c> do produto) e os do catálogo/gestão do SecureGate
/// (Fase 6.3): <c>catalog:&lt;produto&gt;</c> concede leitura do catálogo daquele produto
/// apenas, e <c>securegate:admin</c> a gestão — todos com resource <c>secco-securegate</c>.
/// Estrutura de operador: o tenant de plataforma e o role de operador de instalação que
/// habilita o scope admin no login (Fase 7.1). Tudo idempotente por nome/id.
/// </summary>
public sealed class SecureGateReferenceDataSeeder(
	IOpenIddictScopeManager scopeManager,
	SecureGateDbContext context) : IReferenceDataSeeder
{
	/// <summary>
	/// Nome de exibição ANTIGO do tenant de plataforma (issue #4/2026-09) — sugeria que a
	/// instalação era "da Secco". Só usado para a convergência abaixo.
	/// </summary>
	private const string LegacyTenantName = "Plataforma Secco";

	/// <summary>Slug ANTIGO do tenant de plataforma. Só usado para a convergência abaixo.</summary>
	private const string LegacyTenantSlug = "plataforma";

	/// <summary>Audience do próprio SecureGate — resource dos scopes de catálogo e gestão.</summary>
	private const string SecureGateResource = "secco-securegate";

	/// <summary>Scopes de produto: nome do scope → audience (resource).</summary>
	private static readonly IReadOnlyDictionary<string, string> ProductScopes = new Dictionary<string, string>
	{
		["logstream"] = "secco-logstream",
		["notificationhub"] = "secco-notificationhub",
		["securegate"] = SecureGateResource,
	};

	/// <summary>Produtos multi-tenant com catálogo servido pelo SecureGate (ADR-0005).</summary>
	private static readonly IReadOnlyList<string> CatalogProducts = ["logstream"];

	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		foreach (var (scopeName, resource) in ProductScopes)
		{
			await UpsertScopeAsync(scopeName, $"Acesso ao produto {resource}", resource, cancellationToken)
				.ConfigureAwait(false);
		}

		foreach (var product in CatalogProducts)
		{
			await UpsertScopeAsync(
				SecureGateScopes.CatalogFor(product),
				$"Leitura do catálogo de tenants do produto {product}",
				SecureGateResource,
				cancellationToken).ConfigureAwait(false);
		}

		await UpsertScopeAsync(
			SecureGateScopes.Admin,
			"Gestão do SecureGate (tenants e bancos)",
			SecureGateResource,
			cancellationToken).ConfigureAwait(false);

		// Fase 6.4 (ADR-0021): resolução role→permissions — scope único de plataforma
		await UpsertScopeAsync(
			SecureGateScopes.AuthorizationRead,
			"Leitura da resolução role→permissions",
			SecureGateResource,
			cancellationToken).ConfigureAwait(false);

		// Fase 7.1 (ADR-0023): estrutura do operador de instalação
		await SeedInstallationOperatorAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Tenant de plataforma + role de operador de instalação (gate do scope admin no login).
	/// Convergência idempotente de vocabulário (issue #4/2026-09, mesmo padrão da ADR-0025 para
	/// segredo legado em claro): tenant e role são localizados por chave estável (Guid/tenant+
	/// nome), e valores antigos gravados por uma instalação anterior são atualizados NO LUGAR
	/// em vez de deixados órfãos ao lado de uma linha nova.
	/// </summary>
	private async Task SeedInstallationOperatorAsync(CancellationToken cancellationToken)
	{
		if (!await context.Tenants.AnyAsync(t => t.Id == SecureGatePlatform.TenantId, cancellationToken).ConfigureAwait(false))
		{
			var tenant = new Tenant(SecureGatePlatform.TenantName, SecureGatePlatform.TenantSlug);
			context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = SecureGatePlatform.TenantId;
			context.Tenants.Add(tenant);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		else
		{
			// Convergência de nome/slug (issue #4/2026-09): localizado por Guid, nunca por slug —
			// só atualiza se ainda estiver nos valores antigos ("Plataforma Secco"/"plataforma"),
			// então rodar de novo depois de convergido não escreve nada (idempotência, ADR-0019).
			var tenant = await context.Tenants
				.SingleAsync(t => t.Id == SecureGatePlatform.TenantId, cancellationToken)
				.ConfigureAwait(false);

			// Campo a campo, e nao com OR sobre os dois: casar apenas o slug legado nao deve
			// sobrescrever um Name que alguem tenha ajustado, e vice-versa.
			var convergiu = false;

			if (tenant.Name == LegacyTenantName)
			{
				context.Entry(tenant).Property(nameof(Tenant.Name)).CurrentValue = SecureGatePlatform.TenantName;
				convergiu = true;
			}

			if (tenant.Slug == LegacyTenantSlug)
			{
				context.Entry(tenant).Property(nameof(Tenant.Slug)).CurrentValue = SecureGatePlatform.TenantSlug;
				convergiu = true;
			}

			if (convergiu)
			{
				await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		var normalized = SecureGatePlatform.OperatorRole.ToUpperInvariant();

		var role = await context.Roles
			.SingleOrDefaultAsync(r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == normalized, cancellationToken)
			.ConfigureAwait(false);

		if (role is not null)
		{
			// Ramo 1: o nome novo já existe — nada a fazer.
			return;
		}

		var legacyNormalized = SecureGatePlatform.LegacyOperatorRole.ToUpperInvariant();

		var legacyRole = await context.Roles
			.SingleOrDefaultAsync(r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == legacyNormalized, cancellationToken)
			.ConfigureAwait(false);

		if (legacyRole is not null)
		{
			// Ramo 2: renomeia NO LUGAR — o Id não muda, e é isso que mantém as atribuições
			// existentes em tb_user_roles apontando para o mesmo role (só o nome mudou de
			// vocabulário, o role continua sendo o mesmo objeto de autorização).
			legacyRole.Name = SecureGatePlatform.OperatorRole;
			legacyRole.NormalizedName = normalized;
			legacyRole.ConcurrencyStamp = Guid.NewGuid().ToString();
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		// Ramo 3: nenhum dos dois existe — cria com o nome novo, como antes.
		// O role só marca o operador — os poderes vêm do scope admin, não de permissões (ADR-0023)
		context.Roles.Add(new Identity.Role
		{
			Id = Guid.CreateVersion7(),
			TenantId = SecureGatePlatform.TenantId,
			Name = SecureGatePlatform.OperatorRole,
			NormalizedName = normalized,
			ConcurrencyStamp = Guid.NewGuid().ToString(),
		});
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task UpsertScopeAsync(
		string scopeName,
		string displayName,
		string resource,
		CancellationToken cancellationToken)
	{
		var descriptor = new OpenIddictScopeDescriptor
		{
			Name = scopeName,
			DisplayName = displayName,
			Resources = { resource },
		};

		var existing = await scopeManager.FindByNameAsync(scopeName, cancellationToken).ConfigureAwait(false);

		if (existing is null)
		{
			await scopeManager.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await scopeManager.UpdateAsync(existing, descriptor, cancellationToken).ConfigureAwait(false);
		}
	}
}
