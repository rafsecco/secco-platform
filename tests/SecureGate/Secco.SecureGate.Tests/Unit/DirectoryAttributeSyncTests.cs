using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Etapa de atributos de pessoa no ciclo (ADR-0055): roda por último, só com grupo de provisionamento, e nunca
/// atrapalha as etapas anteriores nem grava trilha.
/// </summary>
public class DirectoryAttributeSyncTests
{
	private readonly DirectoryReconcilerRig _rig = new();

	private Task<DirectorySyncResult> CycleAsync() => _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

	private int AttributeReads() => _rig.Graph.ReceivedCalls()
		.Count(call => call.GetMethodInfo().Name == nameof(IEntraGroupDirectory.GetGroupMemberAttributesAsync));

	private static DirectoryAttributes Attrs(Guid oid, string? title = "Analista") => new(oid, title, null, null, null);

	[Fact]
	public async Task Etapa_SemGrupoDeProvisionamento_NaoRoda()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "base");
		var account = _rig.ManagedAccount();
		_rig.GraphMembers(group, account.ObjectId);

		var result = await CycleAsync();

		result.Attributes.Should().BeNull();
		AttributeReads().Should().Be(0);
	}

	[Fact]
	public async Task Etapa_Forbidden_ConsentRequired_ContasEPerfisJaAplicados()
	{
		var group = Guid.NewGuid();
		var person = new DirectoryPerson(Guid.NewGuid(), "Maria", "maria@secco.test", null);
		_rig.Map(group, "base", provisioning: true);
		_rig.GraphPeople(group, person);
		_rig.GraphAttributesFail(group, SecureGateErrors.Federation.ConsentRequired);

		var result = await CycleAsync();

		result.Attributes!.Outcome.Should().Be(DirectoryAttributeSyncOutcome.ConsentRequired);
		result.Created.Should().Be(1);
		result.Added.Should().Be(1);
		_rig.AttributeStore.Applied.Should().BeEmpty();
	}

	[Fact]
	public async Task Etapa_FalhaDeLeitura_NadaGravado()
	{
		var group = Guid.NewGuid();
		var account = _rig.ManagedAccount();
		_rig.Map(group, "base", provisioning: true);
		_rig.GraphPeople(group, new DirectoryPerson(account.ObjectId, "Ana", "ana@secco.test", null));
		_rig.GraphAttributesFail(group, SecureGateErrors.Federation.DirectoryUnavailable);

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Attributes!.Outcome.Should().Be(DirectoryAttributeSyncOutcome.ReadFailed);
		result.Attributes.ErrorCode.Should().Be(SecureGateErrors.Federation.DirectoryUnavailable.Code);
		_rig.AttributeStore.Applied.Should().BeEmpty();
	}

	[Fact]
	public async Task Etapa_FreioDoProvisionamento_EtapaNaoRoda()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "base", provisioning: true);

		// 10 contas geridas saem do grupo de uma vez: o freio de contas aborta o tenant
		for (var i = 0; i < 10; i++)
		{
			_rig.ManagedAccount();
		}

		_rig.GraphPeople(group);

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.AbortedByBrake);
		result.Attributes.Should().BeNull();
		AttributeReads().Should().Be(0);
	}

	[Fact]
	public async Task Etapa_FreioDeEsvaziamento_NadaGravado()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "base", provisioning: true);
		var accounts = Enumerable.Range(0, 10).Select(_ => _rig.ManagedAccount()).ToList();
		_rig.GraphPeople(group, [.. accounts.Select(a => new DirectoryPerson(a.ObjectId, "P", $"{a.UserId:N}@secco.test", null))]);

		// Todos já têm cargo gravado, e o diretório devolve tudo vazio: limpeza em massa
		foreach (var account in accounts)
		{
			_rig.AttributeStore.Stored[account.UserId] = new DirectoryAttributeUpdate(account.UserId, "Cargo", null, null, null);
		}

		_rig.GraphAttributes(group, [.. accounts.Select(a => Attrs(a.ObjectId, null))]);

		var result = await CycleAsync();

		result.Attributes!.Outcome.Should().Be(DirectoryAttributeSyncOutcome.AbortedByBrake);
		_rig.AttributeStore.Applied.Should().BeEmpty();
		_rig.AttributeStore.Stored.Values.Should().OnlyContain(update => update.JobTitle == "Cargo");
	}

	[Fact]
	public async Task Etapa_NaoGravaTrilha()
	{
		var group = Guid.NewGuid();
		var account = _rig.ManagedAccount();
		_rig.Map(group, "base", provisioning: true);
		_rig.GraphPeople(group, new DirectoryPerson(account.ObjectId, "Ana", "ana@secco.test", null));
		_rig.GraphAttributes(group, Attrs(account.ObjectId));

		var result = await CycleAsync();

		result.Attributes!.Outcome.Should().Be(DirectoryAttributeSyncOutcome.Completed);
		result.Attributes.Updated.Should().Be(1);
		_rig.Auditor.Records.Should().NotContain(r => r.Category == "attributes");
		_rig.Events.IndexOf("attributes").Should().BeGreaterThan(_rig.Events.FindLastIndex(e => e.StartsWith("audit:", StringComparison.Ordinal)));
		_rig.Events.Count(e => e.StartsWith("audit:", StringComparison.Ordinal)).Should().Be(_rig.Auditor.Records.Count);
	}

	[Fact]
	public async Task Ciclo_ContaCriadaNoCiclo_JaRecebeAtributos()
	{
		var group = Guid.NewGuid();
		var person = new DirectoryPerson(Guid.NewGuid(), "Maria", "maria@secco.test", null);
		_rig.Map(group, "base", provisioning: true);
		_rig.GraphPeople(group, person);
		_rig.GraphAttributes(group, Attrs(person.ObjectId, "Gerente"));

		var result = await CycleAsync();

		var userId = _rig.AccountStore.Created.Single().UserId;
		result.Attributes!.Updated.Should().Be(1);
		_rig.AttributeStore.Stored[userId].JobTitle.Should().Be("Gerente");
	}
}
