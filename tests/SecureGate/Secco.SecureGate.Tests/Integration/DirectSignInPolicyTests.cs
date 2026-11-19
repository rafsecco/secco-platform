using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Decisão "o authorize vai direto ao Entra?", contra o banco real: só o client vinculado a tenant, com
/// federação habilitada e a opção ligada, resulta em alvo.
/// </summary>
[Collection(EntraFederatedApiCollectionDefinition.Name)]
public class DirectSignInPolicyTests(EntraFederatedSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string RedirectUri = "https://app.testes.local/signin-oidc";

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<(Guid TenantId, string ClientId, Guid DirectoryId)> ScenarioAsync(
		bool federation = true,
		bool enabled = true,
		bool directSignIn = true,
		string[]? domains = null)
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var clientId = $"direct-{Guid.NewGuid():N}";
		var directoryId = Guid.NewGuid();

		await factory.CreateTenantPublicClientAsync(tenantId, clientId, RedirectUri);

		if (federation)
		{
			using var scope = factory.Services.CreatePlatformScope();
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var entity = new TenantFederation(tenantId, directoryId);

			if (domains is not null)
			{
				entity.SetAllowedEmailDomains(domains);
			}

			entity.SetDirectSignIn(directSignIn);
			entity.SetEnabled(enabled);
			context.TenantFederations.Add(entity);
			await context.SaveChangesAsync();
		}

		return (tenantId, clientId, directoryId);
	}

	private async Task<DirectSignInTarget?> ResolveAsync(string? clientId)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await scope.ServiceProvider.GetRequiredService<DirectSignInPolicy>()
			.ResolveAsync(clientId, CancellationToken.None);
	}

	[Fact]
	public async Task Resolve_ClientDeTenantComDirectSignIn_DevolveDiretorioEDominio()
	{
		var s = await ScenarioAsync(domains: ["abbc.com.br", "outro.com.br"]);

		var target = await ResolveAsync(s.ClientId);

		target.Should().Be(new DirectSignInTarget(s.TenantId, s.DirectoryId, "abbc.com.br"));
	}

	[Fact]
	public async Task Resolve_SemDominios_DomainHintNulo()
	{
		var s = await ScenarioAsync();

		var target = await ResolveAsync(s.ClientId);

		target.Should().NotBeNull();
		target!.DomainHint.Should().BeNull();
		target.DirectoryId.Should().Be(s.DirectoryId);
	}

	[Fact]
	public async Task Resolve_DirectSignInDesligado_Nulo()
	{
		var s = await ScenarioAsync(directSignIn: false);

		(await ResolveAsync(s.ClientId)).Should().BeNull();
	}

	[Fact]
	public async Task Resolve_FederacaoDesabilitada_Nulo()
	{
		var s = await ScenarioAsync(enabled: false);

		(await ResolveAsync(s.ClientId)).Should().BeNull();
	}

	[Fact]
	public async Task Resolve_TenantSemFederacao_Nulo()
	{
		var s = await ScenarioAsync(federation: false);

		(await ResolveAsync(s.ClientId)).Should().BeNull();
	}

	[Fact]
	public async Task Resolve_ClientSemTenant_Nulo()
	{
		var clientId = $"plat-{Guid.NewGuid():N}";
		await factory.CreatePublicClientAsync(clientId, RedirectUri);

		(await ResolveAsync(clientId)).Should().BeNull();
	}

	[Fact]
	public async Task Resolve_ClientInexistente_Nulo()
	{
		(await ResolveAsync($"nao-existe-{Guid.NewGuid():N}")).Should().BeNull();
	}
}
