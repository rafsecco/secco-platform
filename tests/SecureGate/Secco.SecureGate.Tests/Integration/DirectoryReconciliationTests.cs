using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Reconciliação de perfis no login federado (ADR-0055, entrega A), contra o banco real e um Graph falso: a decisão
/// acontece, a trilha local registra com ator <c>directory</c>, a sessão cai na retirada, e o Graph fora do ar nunca
/// derruba o login nem muda perfil.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class DirectoryReconciliationTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private sealed record Scenario(Guid TenantId, Guid DirectoryId, Guid GroupId, string RoleName, Guid UserId, Guid ObjectId, string Email);

	/// <summary>Tenant com federação habilitada, grupo mapeado a um perfil e uma conta pré-provisionada (ainda sem vínculo).</summary>
	private async Task<Scenario> ScenarioAsync(string roleName = "financeiro", bool mapped = true, params string[] manualRoles)
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, roleName);

		foreach (var manual in manualRoles.Where(r => !string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)))
		{
			await IdentitySeed.RoleAsync(factory, tenantId, manual);
		}

		var email = $"rec-{Guid.NewGuid():N}@secco.test";
		var userId = await IdentitySeed.UserAsync(factory, tenantId, email, manualRoles);
		var directoryId = Guid.NewGuid();
		var groupId = Guid.NewGuid();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		context.TenantFederations.Add(new TenantFederation(tenantId, directoryId));

		if (mapped)
		{
			var normalized = roleName.ToUpperInvariant();
			var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized);
			context.TenantGroupRoleMappings.Add(new TenantGroupRoleMapping(tenantId, groupId, "Grupo do diretório", role.Id));
		}

		await context.SaveChangesAsync();

		return new Scenario(tenantId, directoryId, groupId, roleName, userId, Guid.NewGuid(), email);
	}

	private static ClaimsPrincipal Principal(Scenario s) => new(new ClaimsIdentity(
		[
			new Claim("tid", s.DirectoryId.ToString("D")),
			new Claim("oid", s.ObjectId.ToString("D")),
			new Claim("email", s.Email),
		],
		EntraSignInProcessor.LoginProvider));

	private async Task<Secco.SharedKernel.Results.Result<User>> LoginAsync(Scenario s)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await scope.ServiceProvider.GetRequiredService<EntraSignInProcessor>().ProcessAsync(Principal(s));
	}

	private async Task<IReadOnlyList<(string Role, RoleAssignmentOrigin Origin, Guid? Source)>> AssignmentsAsync(Scenario s)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var rows = await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userRole.UserId == s.UserId && role.TenantId == s.TenantId
			select new { role.Name, userRole.Origin, userRole.SourceGroupId })
			.ToListAsync();

		return [.. rows.Select(r => (r.Name!, r.Origin, r.SourceGroupId))];
	}

	private async Task<IReadOnlyList<Secco.SecureGate.Domain.TenantAdministration.AuthorityAuditEntry>> TrailAsync(Scenario s)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.AuthorityAuditEntries.AsNoTracking()
			.Where(e => e.TenantId == s.TenantId && e.ActorType == "directory")
			.OrderBy(e => e.CreatedAt)
			.ToListAsync();
	}

	private async Task<string?> SecurityStampAsync(Guid userId)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString()))!.SecurityStamp;
	}

	// ---- Acrescentar ----

	[Fact]
	public async Task Login_QuemEstaNoGrupoMapeado_GanhaOPerfilComOrigemDirectory_EATrilhaRegistraComAtorDiretorio()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);

		(await LoginAsync(s)).IsSuccess.Should().BeTrue();

		(await AssignmentsAsync(s)).Should().ContainSingle()
			.Which.Should().Be(("financeiro", RoleAssignmentOrigin.Directory, (Guid?)s.GroupId));

		var entry = (await TrailAsync(s)).Should().ContainSingle().Subject;
		entry.ActorType.Should().Be("directory");
		entry.ActorId.Should().Be(s.DirectoryId.ToString("D"));
		entry.Action.Should().Be("sincronizacao-do-diretorio.perfil-acrescentado");
		entry.ResourceType.Should().Be("user");
		entry.ResourceId.Should().Be(s.UserId.ToString("D"));
		var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
		metadata.GetProperty("trigger").GetString().Should().Be("login");
		metadata.GetProperty("role").GetString().Should().Be("financeiro");
		metadata.GetProperty("groupId").GetGuid().Should().Be(s.GroupId);
	}

	[Fact]
	public async Task Login_RepetidoSemMudancaNoDiretorio_EIdempotente_NaoRepeteATrilha()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);

		await LoginAsync(s);
		await LoginAsync(s);

		(await AssignmentsAsync(s)).Should().ContainSingle();
		(await TrailAsync(s)).Should().ContainSingle();
	}

	[Fact]
	public async Task Login_SemMapeamentoNoTenant_NaoChamaOGraphEOLoginSegue()
	{
		var s = await ScenarioAsync(mapped: false);

		(await LoginAsync(s)).IsSuccess.Should().BeTrue();

		factory.Graph.CallsFor(s.DirectoryId).Should().Be(0);
		(await AssignmentsAsync(s)).Should().BeEmpty();
	}

	// ---- Retirar ----

	[Fact]
	public async Task Login_QuemSaiuDoGrupo_PerdeOPerfilDirectory_EAsSessoesCaem_MasOManualFica()
	{
		var s = await ScenarioAsync("financeiro", true, "gestor");

		// Outro membro ativo no perfil: sem ele a guarda do último membro seguraria a retirada
		await IdentitySeed.UserAsync(factory, s.TenantId, $"colega-{Guid.NewGuid():N}@secco.test", "financeiro");
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		await LoginAsync(s);
		var stampBefore = await SecurityStampAsync(s.UserId);

		factory.Graph.SetMembers(s.DirectoryId, s.GroupId);
		(await LoginAsync(s)).IsSuccess.Should().BeTrue();

		(await AssignmentsAsync(s)).Select(a => a.Role).Should().Equal("gestor");
		(await SecurityStampAsync(s.UserId)).Should().NotBe(stampBefore, "a retirada revoga as sessões (ADR-0032)");
		(await TrailAsync(s)).Select(e => e.Action).Should().Equal(
			"sincronizacao-do-diretorio.perfil-acrescentado", "sincronizacao-do-diretorio.perfil-retirado");
	}

	[Fact]
	public async Task Login_PerfilManualNuncaESaiMesmoSemEstarNoGrupo()
	{
		var s = await ScenarioAsync("financeiro", true, "financeiro");
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId);

		await LoginAsync(s);

		(await AssignmentsAsync(s)).Should().ContainSingle().Which.Origin.Should().Be(RoleAssignmentOrigin.Manual);
		(await TrailAsync(s)).Should().BeEmpty();
	}

	[Fact]
	public async Task Login_UnicoMembroAtivoDoPerfil_NaoPerdeOPerfil_ERetiradaBarradaFicaNaTrilha()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		await LoginAsync(s);
		var stampBefore = await SecurityStampAsync(s.UserId);

		factory.Graph.SetMembers(s.DirectoryId, s.GroupId);
		await LoginAsync(s);

		(await AssignmentsAsync(s)).Should().ContainSingle("o perfil não fica sem nenhum membro ativo");
		(await SecurityStampAsync(s.UserId)).Should().Be(stampBefore, "retirada barrada não derruba a sessão");
		(await TrailAsync(s))[^1].Action.Should().Be("sincronizacao-do-diretorio.retirada-barrada");
	}

	[Fact]
	public async Task Login_ComOutroMembroAtivoNoPerfil_RetiraNormalmente()
	{
		var s = await ScenarioAsync();
		var colleague = await IdentitySeed.UserAsync(factory, s.TenantId, $"colega-{Guid.NewGuid():N}@secco.test", "financeiro");
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		await LoginAsync(s);

		factory.Graph.SetMembers(s.DirectoryId, s.GroupId);
		await LoginAsync(s);

		(await AssignmentsAsync(s)).Select(a => a.Role).Should().NotContain("financeiro");
		colleague.Should().NotBeEmpty();
	}

	[Fact]
	public async Task Login_OutroMembroDesativado_NaoContaComoMembroAtivo_RetiradaBarrada()
	{
		var s = await ScenarioAsync();
		var colleague = await IdentitySeed.UserAsync(factory, s.TenantId, $"colega-{Guid.NewGuid():N}@secco.test", "financeiro");
		await IdentitySeed.DeactivateAsync(factory, colleague);
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		await LoginAsync(s);

		factory.Graph.SetMembers(s.DirectoryId, s.GroupId);
		await LoginAsync(s);

		(await AssignmentsAsync(s)).Should().ContainSingle("o único outro membro está desativado");
	}

	// ---- Fail-closed ----

	[Fact]
	public async Task Login_ComGraphForaDoAr_SegueSemMudarPerfilNenhum_ESemLinhaNaTrilha()
	{
		var s = await ScenarioAsync("financeiro", true, "gestor");
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		await LoginAsync(s);

		factory.Graph.SetFailure(s.DirectoryId, s.GroupId, SecureGateErrors.Federation.DirectoryUnavailable);
		var stampBefore = await SecurityStampAsync(s.UserId);
		var trailBefore = (await TrailAsync(s)).Count;

		(await LoginAsync(s)).IsSuccess.Should().BeTrue("falha do Graph não derruba o login");

		(await AssignmentsAsync(s)).Select(a => a.Role).Should().BeEquivalentTo(["financeiro", "gestor"]);
		(await SecurityStampAsync(s.UserId)).Should().Be(stampBefore);
		(await TrailAsync(s)).Should().HaveCount(trailBefore, "queda do Graph não escreve uma linha por login; o ciclo registra a falha");
	}

	[Fact]
	public async Task Login_ComGraphQueLancaExcecao_ELoginSegueMesmoAssim()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		factory.Graph.ThrowFor(s.DirectoryId);

		(await LoginAsync(s)).IsSuccess.Should().BeTrue();

		(await AssignmentsAsync(s)).Should().BeEmpty();
	}

	[Fact]
	public async Task Login_RecusadoPelaFederacao_NaoReconciliaNada()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		var wrongTenantPrincipal = Principal(s with { DirectoryId = Guid.NewGuid() });

		using var scope = factory.Services.CreatePlatformScope();
		var result = await scope.ServiceProvider.GetRequiredService<EntraSignInProcessor>().ProcessAsync(wrongTenantPrincipal);

		result.IsFailure.Should().BeTrue("o tid não é o diretório registrado");
		(await AssignmentsAsync(s)).Should().BeEmpty();
		factory.Graph.CallsFor(s.DirectoryId).Should().Be(0);
	}

	[Fact]
	public async Task RemoverPerfilDeOrigemDirectoryPelaApi_ContinuaRecusado()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetMembers(s.DirectoryId, s.GroupId, s.ObjectId);
		await LoginAsync(s);

		var admin = IdentitySeed.AdminClient(factory);
		var response = await admin.DeleteAsync($"/api/v1/tenants/{s.TenantId}/users/{s.UserId}/roles/financeiro");

		response.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
		(await AssignmentsAsync(s)).Should().ContainSingle();
	}
}
