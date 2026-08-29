using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.Testing;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV)
/// sobre a base da plataforma (ADR-0027), hospedando o banco de PLATAFORMA
/// <c>secco_securegate</c> (ADR-0022 — identidade não é dado de tenant).
/// Herdável: <see cref="SelfIssuedAuthSecureGateApiFactory"/> troca a chave HS256 de
/// testes pela Authority do próprio servidor.
/// </summary>
public class SecureGateApiFactory : SeccoApiFactory<Program>
{
	/// <inheritdoc />
	protected override string Audience => "secco-securegate";

	/// <summary>Connection string do banco de PLATAFORMA (ADR-0022 — identidade não é dado de tenant).</summary>
	public string GetPlatformConnectionString() => GetConnectionStringFor("secco_securegate");

	/// <summary>Aplica migrations + seed de referência (scopes) — a base garante a chamada única.</summary>
	protected override async Task MigrateAsync(IServiceProvider services)
	{
		await Secco.SecureGate.Infrastructure.SecureGateInfrastructureExtensions
			.MigrateSecureGateDatabaseAsync(services);

		// Seed de referência (scopes de produto); o de DEV não roda em Testing (guarda dupla)
		await Secco.SDK.EntityFrameworkCore.Seeding.SeccoSeedingExtensions
			.SeedSeccoDataAsync(services);
	}

	/// <summary>Registra um client OIDC de teste (client credentials) com os scopes informados.</summary>
	public Task CreateClientAsync(string clientId, string clientSecret, params string[] scopes) =>
		CreateClientWithRolesAsync(clientId, clientSecret, roles: null, scopes);

	/// <summary>
	/// Registra um client OIDC de teste com scopes e roles (Fase 6.4, ADR-0021 —
	/// máquinas carregam a claim curta <c>role</c> como os usuários). Nome distinto por
	/// design: um overload posicional confundiria scope com roles.
	/// </summary>
	public async Task CreateClientWithRolesAsync(string clientId, string clientSecret, string? roles, params string[] scopes)
	{
		using var scope = Services.CreateScope();
		var applications = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();

		if (await applications.FindByClientIdAsync(clientId) is not null)
		{
			return;
		}

		var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientSecret = clientSecret,
			DisplayName = $"Client de teste {clientId}",
		};

		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token);
		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);

		foreach (var scopeName in scopes)
		{
			descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
		}

		await applications.CreateAsync(descriptor);

		if (!string.IsNullOrWhiteSpace(roles)
			&& await applications.FindByClientIdAsync(clientId)
				is Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication application)
		{
			application.Roles = roles;
			await applications.UpdateAsync(application);
		}
	}

	/// <summary>
	/// Registra um client PÚBLICO de teste (authorization code + PKCE + refresh, sem secret) —
	/// o modelo de uma aplicação web/SPA (Fase 6.5). Consent implícito (first-party).
	/// </summary>
	public async Task CreatePublicClientAsync(string clientId, string redirectUri, params string[] scopes)
	{
		using var scope = Services.CreateScope();
		var applications = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();

		if (await applications.FindByClientIdAsync(clientId) is not null)
		{
			return;
		}

		var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Public,
			ConsentType = OpenIddict.Abstractions.OpenIddictConstants.ConsentTypes.Implicit,
			DisplayName = $"Client público de teste {clientId}",
			RedirectUris = { new Uri(redirectUri) },
			Permissions =
			{
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Authorization,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.EndSession,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.ResponseTypes.Code,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Email,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Profile,
				OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Roles,
			},
		};

		foreach (var scopeName in scopes)
		{
			descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
		}

		await applications.CreateAsync(descriptor);
	}

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		settings["SecureGate:Database:ConnectionString"] = GetPlatformConnectionString();
	}
}
