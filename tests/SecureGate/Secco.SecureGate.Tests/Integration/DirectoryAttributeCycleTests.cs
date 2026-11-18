using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Federation;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Atributos de pessoa vindos do diretório no ciclo (ADR-0055), contra o banco real (credencial de runtime, RLS
/// valendo) e um Graph falso. Cada teste usa diretório e grupo próprios: o Graph falso é compartilhado pela suíte.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class DirectoryAttributeCycleTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await ResetLeaseAsync();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task ResetLeaseAsync()
	{
		using var scope = factory.Services.CreatePlatformScope();
		await scope.ServiceProvider.GetRequiredService<SecureGateDbContext>().BackgroundLeases
			.Where(l => l.Name == DirectorySyncService.TaskName).ExecuteDeleteAsync();
	}

	private sealed class CapturingLogger : ILogger<DirectorySyncService>
	{
		public List<(int EventId, LogLevel Level)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(
			LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Entries.Add((eventId.Id, logLevel));
	}

	private async Task CycleAsync(ILogger<DirectorySyncService>? logger = null)
	{
		await ResetLeaseAsync();
		var service = new DirectorySyncService(
			factory.Services.GetRequiredService<IServiceScopeFactory>(),
			Options.Create(new DirectorySyncOptions()),
			Options.Create(new SecureGateEntraIdOptions { ClientId = "client-id", ClientSecret = "client-secret" }),
			TimeProvider.System,
			logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DirectorySyncService>.Instance);

		(await service.RunOnceAsync()).Ran.Should().BeTrue();
	}

	private sealed record Scenario(Guid TenantId, Guid DirectoryId, Guid GroupId);

	private async Task<Scenario> ScenarioAsync()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, "base");
		var directoryId = Guid.NewGuid();
		var groupId = Guid.NewGuid();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var federation = new TenantFederation(tenantId, directoryId);
		federation.SetAllowedEmailDomains(["secco.test"]);
		context.TenantFederations.Add(federation);

		var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == "BASE");
		var mapping = new TenantGroupRoleMapping(tenantId, groupId, "Todos os colaboradores", role.Id);
		mapping.MarkAsProvisioningGroup();
		context.TenantGroupRoleMappings.Add(mapping);
		await context.SaveChangesAsync();

		return new Scenario(tenantId, directoryId, groupId);
	}

	private static string Email() => $"attr-{Guid.NewGuid():N}@secco.test";

	private async Task<User> UserByEmailAsync(string email)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email))!;
	}

	[Fact]
	public async Task Ciclo_GravaAtributos_EReflecteMudancaNaRodadaSeguinte()
	{
		var s = await ScenarioAsync();
		var email = Email();
		var person = new DirectoryPerson(Guid.NewGuid(), "Maria da Silva", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);
		factory.Graph.SetAttributes(s.DirectoryId, s.GroupId, new DirectoryAttributes(person.ObjectId, "Analista", "Financeiro", "+55 11 5555-0000", null));

		await CycleAsync();

		var first = await UserByEmailAsync(email);
		first.JobTitle.Should().Be("Analista");
		first.Department.Should().Be("Financeiro");
		first.BusinessPhone.Should().Be("+55 11 5555-0000");
		first.AttributesOrigin.Should().Be(DirectoryAttributesOrigin.Directory);

		factory.Graph.SetAttributes(s.DirectoryId, s.GroupId, new DirectoryAttributes(person.ObjectId, "Gerente", "Financeiro", "+55 11 5555-0000", null));

		await CycleAsync();

		var second = await UserByEmailAsync(email);
		second.JobTitle.Should().Be("Gerente");
		second.Department.Should().Be("Financeiro");
	}

	[Fact]
	public async Task Ciclo_SemConsentimento_ProvisionamentoSegue()
	{
		var s = await ScenarioAsync();
		var email = Email();
		var person = new DirectoryPerson(Guid.NewGuid(), "Sem Consentimento", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);
		factory.Graph.SetAttributes(s.DirectoryId, s.GroupId, new DirectoryAttributes(person.ObjectId, "Analista", "TI", null, null));
		factory.Graph.ForbidAttributes(s.DirectoryId);

		await CycleAsync();

		var user = await UserByEmailAsync(email);
		user.Should().NotBeNull("o provisionamento segue");
		user.JobTitle.Should().BeNull();
		user.Department.Should().BeNull();
		user.AttributesOrigin.Should().Be(DirectoryAttributesOrigin.None);
	}

	[Fact]
	public async Task Ciclo_Atualizacao_EmiteLog3515()
	{
		var s = await ScenarioAsync();
		var person = new DirectoryPerson(Guid.NewGuid(), "Log Um", Email(), null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);
		factory.Graph.SetAttributes(s.DirectoryId, s.GroupId, new DirectoryAttributes(person.ObjectId, "Analista", null, null, null));
		var logger = new CapturingLogger();

		await CycleAsync(logger);

		logger.Entries.Should().Contain((3515, LogLevel.Information));
	}

	[Fact]
	public async Task Ciclo_SemConsentimento_EmiteLog3512()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(Guid.NewGuid(), "Log Dois", Email(), null));
		factory.Graph.ForbidAttributes(s.DirectoryId);
		var logger = new CapturingLogger();

		await CycleAsync(logger);

		logger.Entries.Should().Contain((3512, LogLevel.Warning));
	}

	[Fact]
	public async Task Ciclo_GestorSemConta_EmiteLogs3516E3517()
	{
		var s = await ScenarioAsync();
		var person = new DirectoryPerson(Guid.NewGuid(), "Log Tres", Email(), null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);
		factory.Graph.SetAttributes(
			s.DirectoryId, s.GroupId, new DirectoryAttributes(person.ObjectId, "Analista", null, null, Guid.NewGuid()));
		var logger = new CapturingLogger();

		await CycleAsync(logger);

		logger.Entries.Should().Contain((3516, LogLevel.Warning)).And.Contain((3517, LogLevel.Debug));
	}
}
