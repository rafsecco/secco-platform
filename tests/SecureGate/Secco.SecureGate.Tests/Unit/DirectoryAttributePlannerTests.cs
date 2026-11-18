using FluentAssertions;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Planejamento puro dos atributos do diretório (ADR-0055): o que gravar, que avisos levantar e quando o freio
/// de limpeza em massa dispara. Sem I/O.
/// </summary>
public class DirectoryAttributePlannerTests
{
	private static DirectoryAttributeAccount Account(
		Guid? userId = null,
		Guid? objectId = null,
		bool managed = true,
		bool active = true,
		string? title = null,
		string? department = null,
		string? phone = null,
		Guid? managerId = null,
		DirectoryAttributesOrigin origin = DirectoryAttributesOrigin.Directory) =>
		new(userId ?? Guid.NewGuid(), objectId ?? Guid.NewGuid(), managed, active, title, department, phone, managerId, origin);

	private static DirectoryAttributes Read(
		DirectoryAttributeAccount account,
		string? title = null,
		string? department = null,
		string? phone = null,
		Guid? managerObjectId = null) =>
		new(account.ObjectId, title, department, phone, managerObjectId);

	[Fact]
	public void Gestor_SemGestorNoEntra_NuloSemAviso()
	{
		var a = Account(origin: DirectoryAttributesOrigin.None);

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a)]);

		plan.Updates.Should().ContainSingle().Which.ManagerId.Should().BeNull();
		plan.Notices.Should().BeEmpty();
	}

	[Fact]
	public void Gestor_ContaAtivaDoTenant_Resolvido()
	{
		var boss = Account();
		var a = Account();

		var plan = DirectoryAttributePlanner.Plan([boss, a], [Read(a, managerObjectId: boss.ObjectId)]);

		plan.Updates.Should().ContainSingle().Which.ManagerId.Should().Be(boss.UserId);
		plan.Notices.Should().BeEmpty();
	}

	[Fact]
	public void Gestor_OidSemContaNoTenant_NuloComAviso()
	{
		var a = Account(managerId: Guid.NewGuid());

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a, managerObjectId: Guid.NewGuid())]);

		plan.Updates.Should().ContainSingle().Which.ManagerId.Should().BeNull();
		plan.Notices.Should().ContainSingle().Which.Should().Be(
			new DirectoryAttributeNotice(a.UserId, DirectoryAttributeWarning.ManagerNotFound));
	}

	[Fact]
	public void Gestor_Inativo_NuloComAviso()
	{
		var boss = Account(active: false);
		var a = Account(origin: DirectoryAttributesOrigin.None);

		var plan = DirectoryAttributePlanner.Plan([boss, a], [Read(a, managerObjectId: boss.ObjectId)]);

		plan.Updates.Should().ContainSingle().Which.ManagerId.Should().BeNull();
		plan.Notices.Should().ContainSingle().Which.Should().Be(
			new DirectoryAttributeNotice(a.UserId, DirectoryAttributeWarning.ManagerInactive));
	}

	[Fact]
	public void Gestor_SiMesmo_NuloComAviso()
	{
		var a = Account(origin: DirectoryAttributesOrigin.None);

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a, managerObjectId: a.ObjectId)]);

		plan.Updates.Should().ContainSingle().Which.ManagerId.Should().BeNull();
		plan.Notices.Should().ContainSingle().Which.Should().Be(
			new DirectoryAttributeNotice(a.UserId, DirectoryAttributeWarning.ManagerSelf));
	}

	[Fact]
	public void Gestor_CicloDeDois_AmbosNulos()
	{
		var a = Account(origin: DirectoryAttributesOrigin.None);
		var b = Account(origin: DirectoryAttributesOrigin.None);

		var plan = DirectoryAttributePlanner.Plan(
			[a, b], [Read(a, managerObjectId: b.ObjectId), Read(b, managerObjectId: a.ObjectId)]);

		plan.Updates.Should().HaveCount(2).And.OnlyContain(u => u.ManagerId == null);
		plan.Notices.Should().HaveCount(2).And.OnlyContain(n => n.Warning == DirectoryAttributeWarning.ManagerCycle);
		plan.Notices.Select(n => n.UserId).Should().BeEquivalentTo(new[] { a.UserId, b.UserId });
	}

	[Fact]
	public void Gestor_CicloDeTres_TodosNulos_ForaDoCicloPreservado()
	{
		var a = Account(origin: DirectoryAttributesOrigin.None);
		var b = Account(origin: DirectoryAttributesOrigin.None);
		var c = Account(origin: DirectoryAttributesOrigin.None);
		var d = Account(origin: DirectoryAttributesOrigin.None);

		var plan = DirectoryAttributePlanner.Plan(
			[a, b, c, d],
			[
				Read(a, managerObjectId: b.ObjectId),
				Read(b, managerObjectId: c.ObjectId),
				Read(c, managerObjectId: a.ObjectId),
				Read(d, managerObjectId: a.ObjectId),
			]);

		plan.Updates.Single(u => u.UserId == a.UserId).ManagerId.Should().BeNull();
		plan.Updates.Single(u => u.UserId == b.UserId).ManagerId.Should().BeNull();
		plan.Updates.Single(u => u.UserId == c.UserId).ManagerId.Should().BeNull();
		plan.Updates.Single(u => u.UserId == d.UserId).ManagerId.Should().Be(a.UserId);
		plan.Notices.Should().HaveCount(3).And.OnlyContain(n => n.Warning == DirectoryAttributeWarning.ManagerCycle);
		plan.Notices.Should().NotContain(n => n.UserId == d.UserId);
	}

	[Fact]
	public void Gestor_JaGravadoQueFicouInativo_ZeraNaProximaRodada()
	{
		var boss = Account(active: false);
		var a = Account(managerId: boss.UserId);

		var plan = DirectoryAttributePlanner.Plan([boss, a], [Read(a, managerObjectId: boss.ObjectId)]);

		plan.Updates.Should().ContainSingle().Which.ManagerId.Should().BeNull();
		plan.Notices.Should().ContainSingle().Which.Warning.Should().Be(DirectoryAttributeWarning.ManagerInactive);
		plan.Cleared.Should().Be(0);
	}

	[Fact]
	public void Gestor_NaoGerido_PodeSerGestor()
	{
		var boss = Account(managed: false);
		var a = Account();

		var plan = DirectoryAttributePlanner.Plan([boss, a], [Read(a, managerObjectId: boss.ObjectId)]);

		plan.Updates.Should().ContainSingle().Which.ManagerId.Should().Be(boss.UserId);
		plan.Notices.Should().BeEmpty();
	}

	[Fact]
	public void Gestor_CicloPassandoPorContaNaoAlvo_AlvoZeradoENaoAlvoIntocado()
	{
		var a = Account(origin: DirectoryAttributesOrigin.None);
		var b = Account(managed: false);
		var bWithManager = b with { ManagerId = a.UserId };

		var plan = DirectoryAttributePlanner.Plan([a, bWithManager], [Read(a, managerObjectId: b.ObjectId)]);

		plan.Updates.Should().ContainSingle().Which.UserId.Should().Be(a.UserId);
		plan.Updates[0].ManagerId.Should().BeNull();
		plan.Notices.Should().ContainSingle().Which.Warning.Should().Be(DirectoryAttributeWarning.ManagerCycle);
	}

	[Fact]
	public void Plan_ContaNaoGerida_NaoEhAlvo()
	{
		var a = Account(managed: false, origin: DirectoryAttributesOrigin.None);

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a, title: "Dev")]);

		plan.Updates.Should().BeEmpty();
		plan.ManagedTotal.Should().Be(0);
	}

	[Fact]
	public void Plan_SemMudancaEOrigemJaDirectory_SemUpdate()
	{
		var a = Account(title: "Dev", department: "TI", phone: "123");

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a, title: " Dev ", department: "TI", phone: "123")]);

		plan.Updates.Should().BeEmpty();
		plan.Notices.Should().BeEmpty();
	}

	[Fact]
	public void Plan_PrimeiraVezComTudoNulo_UpdateParaMarcarOrigem()
	{
		var a = Account(origin: DirectoryAttributesOrigin.None);

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a)]);

		plan.Updates.Should().ContainSingle().Which.Should().Be(new DirectoryAttributeUpdate(a.UserId, null, null, null, null));
	}

	[Fact]
	public void Plan_CampoComControle_DescartaComAviso_ContaSegue()
	{
		var a = Account(title: "Antigo");

		var plan = DirectoryAttributePlanner.Plan(
			[a], [Read(a, title: "Dev\u0007", department: "TI\u0007", phone: "123")]);

		var update = plan.Updates.Should().ContainSingle().Subject;
		update.JobTitle.Should().BeNull();
		update.Department.Should().BeNull();
		update.BusinessPhone.Should().Be("123");
		plan.Notices.Should().ContainSingle().Which.Should().Be(
			new DirectoryAttributeNotice(a.UserId, DirectoryAttributeWarning.FieldDiscarded));
		plan.Cleared.Should().Be(0);
	}

	[Fact]
	public void Plan_TelefoneEmBranco_ViraNulo()
	{
		var a = Account(phone: "123");

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a, phone: "  ")]);

		plan.Updates.Should().ContainSingle().Which.BusinessPhone.Should().BeNull();
		plan.Cleared.Should().Be(1);
	}

	private static (List<DirectoryAttributeAccount> Accounts, List<DirectoryAttributes> Reads) Managed(
		int total, int withTitle)
	{
		var accounts = Enumerable.Range(0, total)
			.Select(i => Account(title: i < withTitle ? "Dev" : null)).ToList();
		var reads = accounts.Select(a => Read(a, title: a.JobTitle)).ToList();
		return (accounts, reads);
	}

	[Fact]
	public void Freio_LimpezaCruaAcimaDoPisoEDaFracao_Dispara()
	{
		var (accounts, reads) = Managed(10, 6);
		for (var i = 0; i < 6; i++)
		{
			reads[i] = Read(accounts[i], title: null);
		}

		var plan = DirectoryAttributePlanner.Plan(accounts, reads);

		plan.Cleared.Should().Be(6);
		plan.ManagedTotal.Should().Be(10);
		DirectoryAttributePlanner.ExceedsBrake(plan, new DirectorySyncOptions
		{
			MinRemovalsBeforeBrake = 5, MaxRemovalRatio = 0.1,
		}).Should().BeTrue();
	}

	[Fact]
	public void Freio_NoPiso_NaoDispara()
	{
		var (accounts, reads) = Managed(10, 5);
		for (var i = 0; i < 5; i++)
		{
			reads[i] = Read(accounts[i], title: "");
		}

		var plan = DirectoryAttributePlanner.Plan(accounts, reads);

		plan.Cleared.Should().Be(5);
		DirectoryAttributePlanner.ExceedsBrake(plan, new DirectorySyncOptions
		{
			MinRemovalsBeforeBrake = 5, MaxRemovalRatio = 0.1,
		}).Should().BeFalse();
	}

	[Fact]
	public void Freio_GestorRemovidoNoEntra_ContaUmaVezPorConta()
	{
		var a = Account(title: "Dev", managerId: Guid.NewGuid());

		var plan = DirectoryAttributePlanner.Plan([a], [Read(a)]);

		plan.Cleared.Should().Be(1);
	}

	[Fact]
	public void Freio_NulosPorResolucaoOuDescarte_NaoContam()
	{
		var a = Account(title: "Dev", managerId: Guid.NewGuid());
		var b = Account(department: "TI");

		var plan = DirectoryAttributePlanner.Plan(
			[a, b], [Read(a, title: "Dev", managerObjectId: Guid.NewGuid()), Read(b, department: "TI\u0007")]);

		plan.Cleared.Should().Be(0);
	}

	[Fact]
	public void Freio_PreencherEAlterar_NaoContam()
	{
		var a = Account(title: "Dev");
		var b = Account();

		var plan = DirectoryAttributePlanner.Plan([a, b], [Read(a, title: "Outro"), Read(b, title: "Novo")]);

		plan.Cleared.Should().Be(0);
	}
}
