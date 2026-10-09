using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Comando migrate e recusa de startup com migration pendente (ADR-0038).</summary>
public class SecureGateMigrationTests
{
	private sealed class VerifyingFactory : SecureGateApiFactory
	{
		protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
		{
			base.ConfigureTestConfiguration(settings);
			settings["SecureGate:Database:VerifyMigrationsOnStartup"] = "true";
		}
	}

	[Fact]
	public async Task Startup_ForaDeDevComMigrationPendente_RecusaSubir()
	{
		var factory = new VerifyingFactory();
		await factory.InitializeAsync();   // sobe o SQL Server; NÃO migra

		try
		{
			var act = () => factory.CreateClient();

			act.Should().Throw<InvalidOperationException>().WithMessage("*migrate*");
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}

	[Fact]
	public async Task RunSeccoMigrations_BancoVazio_MigraESemeiaETrue()
	{
		var factory = new SecureGateApiFactory();
		await factory.InitializeAsync();

		try
		{
			_ = factory.CreateClient();   // constrói o host (checagem desligada na factory base)

			var ok = await factory.Services.RunSeccoMigrationsAsync();

			ok.Should().BeTrue();
			using var scope = factory.Services.CreateScope();
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			(await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
			var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
			(await scopes.CountAsync()).Should().BeGreaterThan(0, "o seed de referência registrou os escopos");
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}
}
