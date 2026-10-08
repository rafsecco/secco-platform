using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.OpenIddict;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Reconciliação de clients de plataforma no seed de referência (ADR-0037 + emenda).</summary>
public class PlatformClientReconciliationTests(PlatformClientsSecureGateApiFactory factory)
	: IClassFixture<PlatformClientsSecureGateApiFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private Task<HttpResponseMessage> TokenAsync(string clientId, string secret, string scope) =>
		factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = secret,
			["scope"] = scope,
		}));

	private async Task<OidcApplication?> FindAsync(string clientId)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		return await context.Set<OidcApplication>().AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == clientId);
	}

	[Fact]
	public async Task Seed_ClientDeclarado_ExisteComOrigemConfigurationEObtemToken()
	{
		var machine = await FindAsync(PlatformClientsSecureGateApiFactory.MachineId);

		machine.Should().NotBeNull();
		machine!.Origin.Should().Be(ClientOrigin.Configuration);
		machine.TenantId.Should().BeNull();
		machine.Name.Should().Be(PlatformClientsSecureGateApiFactory.MachineId);
		machine.Roles.Should().Be("leitor");

		(await TokenAsync(PlatformClientsSecureGateApiFactory.MachineId,
			PlatformClientsSecureGateApiFactory.MachineSecret, "catalog:logstream"))
			.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Seed_AuthorizationCode_ExigePkceETemRedirect()
	{
		var portal = await FindAsync(PlatformClientsSecureGateApiFactory.PortalId);

		portal!.RedirectUris.Should().Contain("https://portal.testes.local/signin-oidc");
		portal.Requirements.Should().Contain(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
	}

	[Fact]
	public async Task Seed_ConfigurationNaoDeclarado_EhRemovidoEApiPreservado()
	{
		await factory.CreateClientAsync("orfao-de-configuracao", "orfao-secret-de-32-chars-minimo!!!", "logstream");
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var apiClientId = $"cli_{Guid.NewGuid():N}"[..20];
		await factory.CreateProductClientAsync(tenantId, apiClientId, "api-secret-de-32-chars-minimo!!!!!", null, "logstream");

		await factory.Services.SeedSeccoDataAsync();

		(await FindAsync("orfao-de-configuracao")).Should().BeNull();
		(await FindAsync(apiClientId)).Should().NotBeNull("a reconciliação nunca toca client da API");
	}

	[Fact]
	public async Task Seed_DuasVezesSemMudanca_NaoReHasheiaOSecret()
	{
		var before = (await FindAsync(PlatformClientsSecureGateApiFactory.MachineId))!.ClientSecret;

		await factory.Services.SeedSeccoDataAsync();

		(await FindAsync(PlatformClientsSecureGateApiFactory.MachineId))!.ClientSecret.Should().Be(before);
	}

	[Fact]
	public async Task Seed_SecretAlteradoNoBanco_VoltaAoDaConfiguracao()
	{
		using (var scope = factory.Services.CreateScope())
		{
			var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
			var app = await manager.FindByClientIdAsync(PlatformClientsSecureGateApiFactory.MachineId);
			await manager.UpdateAsync(app!, "outro-secret-qualquer-de-32-chars!!!");
		}

		await factory.Services.SeedSeccoDataAsync();

		(await TokenAsync(PlatformClientsSecureGateApiFactory.MachineId,
			PlatformClientsSecureGateApiFactory.MachineSecret, "catalog:logstream"))
			.StatusCode.Should().Be(HttpStatusCode.OK);
	}
}

public class PlatformClientInvalidConfigurationTests
{
	private sealed class InvalidFactory : SecureGateApiFactory
	{
		protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
		{
			base.ConfigureTestConfiguration(settings);
			settings["SecureGate:PlatformClients:0:ClientId"] = "cli_invasor";
			settings["SecureGate:PlatformClients:0:Type"] = "ClientCredentials";
			settings["SecureGate:PlatformClients:0:ClientSecret"] = "segredo-de-plataforma-com-32-chars!!";
		}
	}

	[Fact]
	public async Task Startup_ClientIdComPrefixoDaApi_Falha()
	{
		var factory = new InvalidFactory();
		await factory.InitializeAsync();

		try
		{
			var act = () => factory.CreateClient();

			act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
		}
		finally
		{
			// Derruba também o container de SQL Server, que o DisposeAsync da WebApplicationFactory não cobre
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}
}
