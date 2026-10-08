using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.OpenIddict;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>
/// Reconcilia os clients de PLATAFORMA com <c>SecureGate:PlatformClients</c> (ADR-0037 + emenda): passo
/// do seed de referência, roda onde ele roda — automático em Development, no processo controlado fora
/// dele (ADR-0005). Cria ou atualiza o declarado e remove client de origem Configuration que saiu da
/// configuração. Nunca toca client da API. Nunca loga secret.
/// </summary>
internal sealed partial class PlatformClientReconciler(
	IOptions<PlatformClientsOptions> options,
	SecureGateDbContext context,
	OpenIddictApplicationManager<OidcApplication> applications,
	IOpenIddictScopeManager scopes,
	ILogger<PlatformClientReconciler> logger) : IReferenceDataSeeder
{
	/// <summary>Depois do registro de escopos (0), antes da re-cifragem do catálogo (100).</summary>
	public int Order => 50;

	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		var declared = options.Value.PlatformClients;
		int created = 0, updated = 0;

		foreach (var definition in declared)
		{
			await EnsureScopesRegisteredAsync(definition, cancellationToken).ConfigureAwait(false);

			var existing = await context.Set<OidcApplication>()
				.FirstOrDefaultAsync(a => a.ClientId == definition.ClientId, cancellationToken).ConfigureAwait(false);

			if (existing is null)
			{
				var application = new OidcApplication
				{
					Origin = ClientOrigin.Configuration,
					Name = definition.ClientId,
					Roles = JoinRoles(definition.Roles),
				};

				await applications.PopulateAsync(application, Describe(definition), cancellationToken).ConfigureAwait(false);

				try
				{
					await applications.CreateAsync(application, definition.ClientSecret!, cancellationToken).ConfigureAwait(false);
					created++;
				}
				catch (DbUpdateException)
				{
					// Outra instância criou no meio (ADR-0035): recarrega e segue como atualização
					context.ChangeTracker.Clear();
					existing = await context.Set<OidcApplication>()
						.FirstAsync(a => a.ClientId == definition.ClientId, cancellationToken).ConfigureAwait(false);
				}
			}

			if (existing is not null)
			{
				if (existing.Origin != ClientOrigin.Configuration)
				{
					// Inalcançável pelo prefixo cli_ proibido na configuração — defesa em profundidade
					throw new InvalidOperationException(
						$"Client de plataforma '{definition.ClientId}' colide com um client registrado pela API.");
				}

				await UpdateAsync(existing, definition, cancellationToken).ConfigureAwait(false);
				updated++;
			}
		}

		var declaredIds = declared.Select(d => d.ClientId!).ToHashSet(StringComparer.Ordinal);
		var stale = await context.Set<OidcApplication>()
			.Where(a => a.Origin == ClientOrigin.Configuration && !declaredIds.Contains(a.ClientId!))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		foreach (var application in stale)
		{
			await applications.DeleteAsync(application, cancellationToken).ConfigureAwait(false);
		}

		var removedIds = string.Join(", ", stale.Select(a => a.ClientId));
		LogReconciled(logger, created, updated, stale.Count, removedIds);
	}

	private async Task UpdateAsync(OidcApplication application, PlatformClientDefinition definition, CancellationToken cancellationToken)
	{
		// Descriptor a partir do estado atual (secret já hasheado): o manager só re-hasheia quando o
		// valor muda — e ele só muda quando o secret declarado não confere mais.
		var descriptor = new OpenIddictApplicationDescriptor();
		await applications.PopulateAsync(descriptor, application, cancellationToken).ConfigureAwait(false);

		var desired = Describe(definition);
		descriptor.DisplayName = desired.DisplayName;
		descriptor.ClientType = desired.ClientType;
		descriptor.ConsentType = desired.ConsentType;
		descriptor.Permissions.Clear();
		descriptor.Permissions.UnionWith(desired.Permissions);
		descriptor.Requirements.Clear();
		descriptor.Requirements.UnionWith(desired.Requirements);
		descriptor.RedirectUris.Clear();
		descriptor.RedirectUris.UnionWith(desired.RedirectUris);
		descriptor.PostLogoutRedirectUris.Clear();
		descriptor.PostLogoutRedirectUris.UnionWith(desired.PostLogoutRedirectUris);

		if (!await applications.ValidateClientSecretAsync(application, definition.ClientSecret!, cancellationToken)
				.ConfigureAwait(false))
		{
			descriptor.ClientSecret = definition.ClientSecret;
		}

		application.Name = definition.ClientId;
		application.Roles = JoinRoles(definition.Roles);

		await applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
	}

	private async Task EnsureScopesRegisteredAsync(PlatformClientDefinition definition, CancellationToken cancellationToken)
	{
		foreach (var scope in definition.Scopes.Where(s => s is not (Scopes.OpenId or Scopes.Profile or Scopes.Email or Scopes.Roles or Scopes.OfflineAccess)))
		{
			if (await scopes.FindByNameAsync(scope, cancellationToken).ConfigureAwait(false) is null)
			{
				throw new InvalidOperationException(
					$"Client de plataforma '{definition.ClientId}' declara o escopo não registrado '{scope}'.");
			}
		}
	}

	private static OpenIddictApplicationDescriptor Describe(PlatformClientDefinition definition)
	{
		var descriptor = new OpenIddictApplicationDescriptor
		{
			ClientId = definition.ClientId,
			ClientType = ClientTypes.Confidential,
			DisplayName = definition.ClientId,
		};

		descriptor.Permissions.Add(Permissions.Endpoints.Token);

		if (definition.Type == PlatformClientType.AuthorizationCode)
		{
			descriptor.ConsentType = ConsentTypes.Implicit;
			descriptor.Permissions.UnionWith(
			[
				Permissions.Endpoints.Authorization,
				Permissions.Endpoints.EndSession,
				Permissions.GrantTypes.AuthorizationCode,
				Permissions.GrantTypes.RefreshToken,
				Permissions.ResponseTypes.Code,
			]);
			descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
			descriptor.RedirectUris.UnionWith(definition.RedirectUris.Select(uri => new Uri(uri)));
			descriptor.PostLogoutRedirectUris.UnionWith(definition.PostLogoutRedirectUris.Select(uri => new Uri(uri)));
		}
		else
		{
			descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
		}

		foreach (var scope in definition.Scopes)
		{
			descriptor.Permissions.Add(scope switch
			{
				Scopes.Email => Permissions.Scopes.Email,
				Scopes.Profile => Permissions.Scopes.Profile,
				Scopes.Roles => Permissions.Scopes.Roles,
				_ => Permissions.Prefixes.Scope + scope,
			});
		}

		return descriptor;
	}

	private static string? JoinRoles(List<string> roles) => roles.Count == 0 ? null : string.Join(' ', roles);

	[LoggerMessage(Level = LogLevel.Information,
		Message = "Clients de plataforma reconciliados: {Created} criados, {Updated} conferidos/atualizados, {Removed} removidos ({RemovedIds}).")]
	private static partial void LogReconciled(ILogger logger, int created, int updated, int removed, string removedIds);
}
