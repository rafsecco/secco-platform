using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Provisionamento de contas pelo diretório (ADR-0055, entrega B): cada regra tem um teste que quebraria sem ela.
/// Tudo em memória — o que se prova é a decisão (o que muda, em que ordem, o que nunca muda).
/// </summary>
public class DirectoryProvisioningTests
{
	private readonly DirectoryReconcilerRig _rig = new();

	private Guid _provisioningGroup;

	private static DirectoryPerson Person(string? mail = "maria@secco.test", string? name = "Maria Souza") =>
		new(Guid.NewGuid(), name, mail, null);

	/// <summary>Grupo de provisionamento mapeado ao perfil "base"; devolve o id do grupo.</summary>
	private Guid ProvisioningGroup(params DirectoryPerson[] people)
	{
		_provisioningGroup = Guid.NewGuid();
		_rig.Map(_provisioningGroup, "base", provisioning: true);
		_rig.GraphPeople(_provisioningGroup, people);

		return _provisioningGroup;
	}

	private Task<DirectorySyncResult> CycleAsync() => _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

	private static string[] Audit(IEnumerable<string> events) =>
		[.. events.Where(e => e.StartsWith("audit:", StringComparison.Ordinal))];

	// ---- Criação ----

	[Fact]
	public async Task Ciclo_PessoaSemConta_CriaContaGerida_ComATrilhaAntesDaCriacao()
	{
		var person = Person();
		ProvisioningGroup(person);

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Created.Should().Be(1);
		var created = _rig.AccountStore.Created.Should().ContainSingle().Subject;
		created.ObjectId.Should().Be(person.ObjectId);
		created.Draft.Email.Should().Be("maria@secco.test");
		created.Draft.DisplayName.Should().Be("Maria Souza");
		_rig.Events.Should().ContainInOrder("audit:conta-criada", "create");

		var record = _rig.Auditor.Records.Single(r => r.Action == DirectorySyncActions.AccountCreated);
		record.UserId.Should().Be(created.UserId, "a trilha aponta para o id que a conta vai ter");
		record.ObjectId.Should().Be(person.ObjectId);
		record.Trigger.Should().Be(DirectorySyncTrigger.Cycle);
		record.Category.Should().Be(DirectorySyncCategories.Accounts);
	}

	[Fact]
	public async Task Ciclo_ContaCriada_RecebeNoMesmoCicloOsPerfisDosGruposMapeados()
	{
		var person = Person();
		ProvisioningGroup(person);
		var other = Guid.NewGuid();
		_rig.Map(other, "financeiro");
		_rig.GraphMembers(other, person.ObjectId);

		var result = await CycleAsync();

		var userId = _rig.AccountStore.Created.Single().UserId;
		result.Added.Should().Be(2, "o perfil do próprio grupo de provisionamento e o do outro grupo");
		_rig.Store.Find(userId, "base")!.Origin.Should().Be(RoleAssignmentOrigin.Directory);
		_rig.Store.Find(userId, "financeiro")!.Origin.Should().Be(RoleAssignmentOrigin.Directory);
		_rig.Events.Should().ContainInOrder("audit:conta-criada", "create", "audit:perfil-acrescentado", "add");
	}

	[Fact]
	public async Task Ciclo_DominioForaDaLista_NaoCria_ERegistraSoOOid()
	{
		var person = Person("maria@outra.com");
		ProvisioningGroup(person);

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		result.Refused.Should().Be(1);
		_rig.Events.Should().NotContain("create");
		var record = _rig.Auditor.Records.Should().ContainSingle().Subject;
		record.Action.Should().Be(DirectorySyncActions.EmailRefused);
		record.Reason.Should().Be("domain-not-allowed");
		record.ObjectId.Should().Be(person.ObjectId);
		record.UserId.Should().BeNull("ainda não há conta");
		record.ToString().Should().NotContain("outra.com", "o e-mail da pessoa nunca vai à trilha");
	}

	[Fact]
	public async Task Ciclo_SemEmailUtilizavel_NaoCria_ERegistra()
	{
		ProvisioningGroup(new DirectoryPerson(Guid.NewGuid(), "Sem Email", null, "sem-arroba"));

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		_rig.Auditor.Records.Should().ContainSingle().Which.Reason.Should().Be("no-usable-email");
	}

	[Fact]
	public async Task Ciclo_ListaDeDominiosVazia_NaoCriaNinguem()
	{
		_rig.AllowedDomains.Clear();
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		result.Refused.Should().Be(1);
	}

	[Fact]
	public async Task Ciclo_EmailJaPertenceAOutraConta_NaoCriaSegundaConta_ERegistra()
	{
		_rig.AccountStore.EmailsInUse.Add("maria@secco.test");
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		result.Refused.Should().Be(1);
		_rig.Events.Should().NotContain("create", "a colisão é detectada antes de gravar qualquer coisa");
		var record = _rig.Auditor.Records.Should().ContainSingle().Subject;
		record.Action.Should().Be(DirectorySyncActions.EmailRefused);
		record.Reason.Should().Be("email-in-use");
	}

	[Fact]
	public async Task Ciclo_CorridaPeloEmail_BancoRecusaACriacao_VaiATrilhaComoEmailEmUso()
	{
		_rig.AccountStore.CollideOnCreate = true;
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		result.Refused.Should().Be(1);
		Audit(_rig.Events).Should().Equal("audit:conta-criada", "audit:email-recusado");
		_rig.Store.Accounts.Should().BeEmpty("nenhuma conta ficou para trás");
	}

	[Fact]
	public async Task Ciclo_PessoaJaComContaVinculada_NaoCriaDeNovo()
	{
		var person = Person();
		ProvisioningGroup(person);
		_rig.Store.Accounts.Add(new DirectoryLinkedAccount(Guid.CreateVersion7(), person.ObjectId, DirectoryManaged: true));

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		_rig.Events.Should().NotContain("create");
	}

	[Fact]
	public async Task Ciclo_TrilhaQueFalha_NaoCriaAConta()
	{
		ProvisioningGroup(Person());
		_rig.Auditor.Accept = _ => false;

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		result.Unrecorded.Should().BeGreaterThanOrEqualTo(1);
		_rig.Events.Should().NotContain("create");
	}

	// ---- Adoção ----

	[Fact]
	public async Task Ciclo_ContaVinculadaPresenteNoGrupo_NaoGerida_EAdotada()
	{
		var (user, oid) = _rig.Account();
		ProvisioningGroup(new DirectoryPerson(oid, "Fulano", "fulano@secco.test", null));

		var result = await CycleAsync();

		result.Adopted.Should().Be(1);
		_rig.AccountStore.Account(user).DirectoryManaged.Should().BeTrue();
		_rig.Events.Should().ContainInOrder("audit:conta-adotada", "adopt");
		_rig.Auditor.Records.Single(r => r.Action == DirectorySyncActions.AccountAdopted).UserId.Should().Be(user);
	}

	[Fact]
	public async Task Ciclo_ContaAdministrativaNoGrupo_NuncaEAdotada()
	{
		var (user, oid) = _rig.Account();
		_rig.Grants.IsAdministrativeAccountAsync(user, Arg.Any<CancellationToken>()).Returns(true);
		ProvisioningGroup(new DirectoryPerson(oid, "Admin", "admin@secco.test", null));

		var result = await CycleAsync();

		result.Adopted.Should().Be(0);
		_rig.AccountStore.Account(user).DirectoryManaged.Should().BeFalse();
		_rig.Events.Should().NotContain("adopt");
	}

	// ---- Desativação ----

	[Fact]
	public async Task Ciclo_ContaGeridaForaDoGrupo_EDesativada_RevogandoSessoesAntesDoLockout()
	{
		var (user, _) = _rig.ManagedAccount();
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Deactivated.Should().Be(1);
		_rig.Events.Should().ContainInOrder("audit:conta-desativada", "revoke", "deactivate");
		_rig.Revoker.Revoked.Should().ContainSingle().Which.Should().Be((user, SessionRevocationReason.UserDeactivated));
		var account = _rig.AccountStore.Account(user);
		account.IsDeactivated.Should().BeTrue();
		account.DeactivationOrigin.Should().Be(DeactivationOrigin.Directory);
	}

	[Fact]
	public async Task Ciclo_ContaSoLocal_ForaDoGrupo_NuncaETocada()
	{
		var (user, _) = _rig.Account();
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Deactivated.Should().Be(0);
		_rig.AccountStore.Account(user).IsDeactivated.Should().BeFalse();
		_rig.Revoker.Revoked.Should().BeEmpty();
		_rig.Events.Should().NotContain("deactivate");
	}

	[Fact]
	public async Task Ciclo_ContaGeridaQueVirouAdministrativa_NuncaEDesativada_BarradaERegistrada()
	{
		var (user, _) = _rig.ManagedAccount();
		_rig.Grants.IsAdministrativeAccountAsync(user, Arg.Any<CancellationToken>()).Returns(true);
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Deactivated.Should().Be(0);
		result.DeactivationsBlocked.Should().Be(1);
		_rig.Events.Should().NotContain("deactivate").And.NotContain("revoke");
		var record = _rig.Auditor.Records.Single(r => r.Action == DirectorySyncActions.DeactivationBlocked);
		record.Reason.Should().Be("administrative-account");
		record.UserId.Should().Be(user);
	}

	[Fact]
	public async Task Ciclo_UltimoMembroAtivoDeUmPerfil_NaoEDesativada_BarradaERegistrada()
	{
		var (user, _) = _rig.ManagedAccount();
		var roleId = _rig.Store.RoleId("financeiro");
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Manual);
		_rig.Store.LastActive.Add((roleId, user));
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Deactivated.Should().Be(0);
		result.DeactivationsBlocked.Should().Be(1);
		_rig.AccountStore.Account(user).IsDeactivated.Should().BeFalse();
		_rig.Revoker.Revoked.Should().BeEmpty();
		var record = _rig.Auditor.Records.Single(r => r.Action == DirectorySyncActions.DeactivationBlocked);
		record.Reason.Should().Be("last-active-member");
		record.RoleName.Should().Be("financeiro");
	}

	[Fact]
	public async Task Ciclo_ContaJaDesativada_ForaDoGrupo_NaoRepeteADesativacao()
	{
		_rig.ManagedAccount(deactivated: true, origin: DeactivationOrigin.Manual);
		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Deactivated.Should().Be(0);
		_rig.Events.Should().NotContain("deactivate").And.NotContain("revoke");
	}

	[Fact]
	public async Task Ciclo_TrilhaQueFalha_NaoDesativaNemRevoga()
	{
		_rig.ManagedAccount();
		ProvisioningGroup(Person());
		_rig.Auditor.Accept = record => record.Action != DirectorySyncActions.AccountDeactivated;

		var result = await CycleAsync();

		result.Deactivated.Should().Be(0);
		_rig.Events.Should().NotContain("deactivate").And.NotContain("revoke");
	}

	// ---- Reativação ----

	[Fact]
	public async Task Ciclo_ContaDesativadaPeloDiretorio_DeVoltaAoGrupo_EReativada_ELimpaAOrigem()
	{
		var (user, oid) = _rig.ManagedAccount(deactivated: true, origin: DeactivationOrigin.Directory);
		ProvisioningGroup(new DirectoryPerson(oid, "Fulano", "fulano@secco.test", null));

		var result = await CycleAsync();

		result.Reactivated.Should().Be(1);
		var account = _rig.AccountStore.Account(user);
		account.IsDeactivated.Should().BeFalse();
		account.DeactivationOrigin.Should().Be(DeactivationOrigin.None);
		_rig.Events.Should().ContainInOrder("audit:conta-reativada", "reactivate");
	}

	[Theory]
	[InlineData(DeactivationOrigin.Manual)]
	[InlineData(DeactivationOrigin.None)]
	public async Task Ciclo_ContaDesativadaPorOutraOrigem_DeVoltaAoGrupo_NuncaEReativada(DeactivationOrigin origin)
	{
		var (user, oid) = _rig.ManagedAccount(deactivated: true, origin: origin);
		ProvisioningGroup(new DirectoryPerson(oid, "Fulano", "fulano@secco.test", null));

		var result = await CycleAsync();

		result.Reactivated.Should().Be(0);
		_rig.AccountStore.Account(user).IsDeactivated.Should().BeTrue("a decisão humana não é desfeita pela sincronização");
		_rig.Events.Should().NotContain("reactivate");
	}

	// ---- Freio de massa ----

	[Fact]
	public async Task Freio_DesativacoesAcimaDoPisoEDaFracao_AbortaOTenantInteiro()
	{
		// 10 contas geridas, só 3 no grupo: 7 desativações (> piso 5 e 70% > 10%)
		var inGroup = Enumerable.Range(0, 3).Select(_ => _rig.ManagedAccount()).ToList();

		for (var i = 0; i < 7; i++)
		{
			_rig.ManagedAccount();
		}

		var newcomer = Person("nova@secco.test");
		ProvisioningGroup(
			[.. inGroup.Select(a => new DirectoryPerson(a.ObjectId, "Membro", $"{a.ObjectId:N}@secco.test", null)), newcomer]);

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.AbortedByBrake);
		_rig.Events.Should().NotContain("create", "nem cria").And.NotContain("deactivate", "nem desativa").And.NotContain("add");
		_rig.Revoker.Revoked.Should().BeEmpty();
		var record = _rig.Auditor.Records.Should().ContainSingle().Subject;
		record.Action.Should().Be(DirectorySyncActions.CycleAborted);
		record.Category.Should().Be(DirectorySyncCategories.Accounts);
		record.Count.Should().Be(7);
		record.Total.Should().Be(10);
	}

	[Fact]
	public async Task Freio_DesativacoesAbortamTambemOsPerfisEAsReativacoes()
	{
		// Há uma reativação e uma inclusão de perfil pendentes: o freio das desativações segura tudo
		var (reactivable, reactivableOid) = _rig.ManagedAccount(deactivated: true, origin: DeactivationOrigin.Directory);
		var (member, memberOid) = _rig.ManagedAccount();

		for (var i = 0; i < 6; i++)
		{
			_rig.ManagedAccount();
		}

		ProvisioningGroup(
			new DirectoryPerson(reactivableOid, "Reativavel", "reativavel@secco.test", null),
			new DirectoryPerson(memberOid, "Membro", "membro@secco.test", null));

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.AbortedByBrake);
		_rig.AccountStore.Account(reactivable).IsDeactivated.Should().BeTrue("a reativação também fica para depois");
		_rig.Store.Find(member, "base").Should().BeNull("nem mexe em perfil");
		_rig.Events.Should().NotContain("reactivate").And.NotContain("add");
	}

	[Fact]
	public async Task Freio_AteOPiso_NaoDispara_MesmoSendoMaiorQueAFracao()
	{
		// 5 contas geridas e o grupo vazio: 5 desativações, 100% — mas o piso (5) protege o tenant pequeno
		for (var i = 0; i < 5; i++)
		{
			_rig.ManagedAccount();
		}

		ProvisioningGroup();

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Deactivated.Should().Be(5);
	}

	[Fact]
	public async Task Freio_AcimaDoPisoMasAbaixoDaFracao_NaoDispara()
	{
		// 100 contas geridas, 6 saíram do grupo: 6 > piso 5, mas 6% < 10%
		var staying = Enumerable.Range(0, 94).Select(_ => _rig.ManagedAccount()).ToList();

		for (var i = 0; i < 6; i++)
		{
			_rig.ManagedAccount();
		}

		ProvisioningGroup(
			[.. staying.Select(a => new DirectoryPerson(a.ObjectId, "Membro", $"{a.ObjectId:N}@secco.test", null))]);

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Deactivated.Should().Be(6);
	}

	[Fact]
	public async Task Freio_DeRetiradaDePerfil_AbortaTambemAsCriacoesDeConta()
	{
		// 10 atribuições Directory de um grupo vazio (todas a retirar) + uma pessoa nova a criar: nada acontece
		var extra = Guid.NewGuid();
		_rig.Map(extra, "extra");
		_rig.GraphMembers(extra);

		for (var i = 0; i < 10; i++)
		{
			var (user, _) = _rig.Account();
			_rig.Store.Has(user, "extra", RoleAssignmentOrigin.Directory, extra);
		}

		ProvisioningGroup(Person());

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.AbortedByBrake);
		_rig.Events.Should().NotContain("create").And.NotContain("remove");
		_rig.Auditor.Records.Should().ContainSingle().Which.Category.Should().Be(DirectorySyncCategories.Roles);
	}

	// ---- Fail-closed na leitura ----

	[Fact]
	public async Task LeituraDoGrupoDeProvisionamento_QueFalha_NaoMudaNadaNoTenant()
	{
		var (user, _) = _rig.ManagedAccount();
		_provisioningGroup = Guid.NewGuid();
		_rig.Map(_provisioningGroup, "base", provisioning: true);
		_rig.Graph.GetGroupMembersWithProfileAsync(_rig.DirectoryId, _provisioningGroup, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Failure<IReadOnlyList<DirectoryPerson>>(SecureGateErrors.Federation.DirectoryUnavailable)));

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.ReadFailed);
		_rig.AccountStore.Account(user).IsDeactivated.Should().BeFalse("lista não lida não pode desativar ninguém");
		_rig.Events.Should().NotContain("create").And.NotContain("deactivate").And.NotContain("add");
		_rig.Auditor.Records.Should().ContainSingle().Which.Action.Should().Be(DirectorySyncActions.ReadFailed);
	}

	[Fact]
	public async Task LeituraDeOutroGrupoMapeado_QueFalha_TambemNaoMudaNadaNasContas()
	{
		var (user, _) = _rig.ManagedAccount();
		ProvisioningGroup(Person());
		var other = Guid.NewGuid();
		_rig.Map(other, "financeiro");
		_rig.GraphMembersFail(other, SecureGateErrors.Federation.DirectoryUnavailable);

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.ReadFailed);
		_rig.Events.Should().NotContain("create").And.NotContain("deactivate");
		_rig.AccountStore.Account(user).IsDeactivated.Should().BeFalse();
	}

	[Fact]
	public async Task SemConsentimentoDePerfil_OProvisionamentoRecusa_EOsPerfisSeguem()
	{
		// User.ReadBasic.All ausente (403 no perfil): não cria, não adota, não desativa — mas a reconciliação de
		// perfis da entrega A continua pelos ids
		var (member, memberOid) = _rig.Account();
		var (managedOut, _) = _rig.ManagedAccount();
		_provisioningGroup = Guid.NewGuid();
		_rig.Map(_provisioningGroup, "base", provisioning: true);
		_rig.GraphProfileForbidden(_provisioningGroup, memberOid, Guid.NewGuid());

		var result = await CycleAsync();

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Created.Should().Be(0);
		result.Adopted.Should().Be(0);
		result.Deactivated.Should().Be(0);
		_rig.AccountStore.Account(managedOut).IsDeactivated.Should().BeFalse();
		_rig.AccountStore.Account(member).DirectoryManaged.Should().BeFalse();
		result.Added.Should().Be(1, "o perfil do grupo continua sendo reconciliado");
		_rig.Store.Find(member, "base").Should().NotBeNull();

		var record = _rig.Auditor.Records.Single(r => r.Action == DirectorySyncActions.ReadFailed);
		record.ErrorCode.Should().Be(SecureGateErrors.Federation.ConsentRequired.Code);
		record.Category.Should().Be(DirectorySyncCategories.Accounts);
	}

	// ---- Marca de provisionamento ----

	[Fact]
	public async Task GrupoNaoMarcado_NaoCriaNemDesativa_ENemLePerfilDeNinguem()
	{
		var (user, _) = _rig.ManagedAccount();
		var group = Guid.NewGuid();
		_rig.Map(group, "base");
		_rig.GraphMembers(group, Guid.NewGuid());

		var result = await CycleAsync();

		result.Created.Should().Be(0);
		result.Deactivated.Should().Be(0);
		_rig.AccountStore.Account(user).IsDeactivated.Should().BeFalse("desmarcar desliga o provisionamento e a desativação");
		await _rig.Graph.DidNotReceiveWithAnyArgs().GetGroupMembersWithProfileAsync(default, default, default);
	}

	// ---- Login ----

	[Fact]
	public async Task Login_NuncaCriaNemDesativaConta_SoReconciliaPerfil()
	{
		var (managedOut, _) = _rig.ManagedAccount();
		var (user, oid) = _rig.Account();
		ProvisioningGroup(Person());
		_rig.GraphChecks(oid, _provisioningGroup);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Created.Should().Be(0);
		result.Deactivated.Should().Be(0);
		_rig.AccountStore.Account(managedOut).IsDeactivated.Should().BeFalse();
		_rig.Events.Should().NotContain("create").And.NotContain("deactivate").And.NotContain("adopt");
	}

	// ---- Primeiro login (JIT) ----

	private static ProvisioningTarget Target(Guid tenantId, Guid groupId, params string[] domains) =>
		new(tenantId, groupId, "base", domains.Length == 0 ? ["secco.test"] : domains);

	private void InGroups(Guid oid, params Guid[] groups) =>
		_rig.Graph.CheckMemberGroupsAsync(_rig.DirectoryId, oid, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var asked = (IReadOnlyCollection<Guid>)call[2];

				return Task.FromResult(Result.Success<IReadOnlySet<Guid>>(asked.Where(candidate => groups.Contains(candidate)).ToHashSet()));
			});

	private void Profile(DirectoryPerson person) =>
		_rig.Graph.GetPersonAsync(_rig.DirectoryId, person.ObjectId, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Success(person)));

	[Fact]
	public async Task Jit_PessoaNoGrupoDeProvisionamento_CriaAContaEODevolve()
	{
		var person = Person();
		var group = Guid.NewGuid();
		_rig.AccountStore.Targets.Add(Target(_rig.TenantId, group));
		InGroups(person.ObjectId, group);
		Profile(person);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.Created);
		result.UserId.Should().Be(_rig.AccountStore.Created.Single().UserId);
		_rig.Events.Should().ContainInOrder("audit:conta-criada", "create");
		_rig.Auditor.Records.Single().Trigger.Should().Be(DirectorySyncTrigger.Login);
	}

	[Fact]
	public async Task Jit_EmVariosTenantsDoMesmoDiretorio_VenceOPrimeiroNaOrdem_ENaoConsultaOsDemais()
	{
		var person = Person();
		var (firstTenant, firstGroup) = (Guid.CreateVersion7(), Guid.NewGuid());
		var (secondTenant, secondGroup) = (Guid.CreateVersion7(), Guid.NewGuid());
		_rig.AccountStore.Targets.Add(Target(firstTenant, firstGroup));
		_rig.AccountStore.Targets.Add(Target(secondTenant, secondGroup));
		InGroups(person.ObjectId, firstGroup, secondGroup);
		Profile(person);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.Created);
		_rig.Auditor.Records.Single().TenantId.Should().Be(firstTenant);
		await _rig.Graph.DidNotReceive().CheckMemberGroupsAsync(
			_rig.DirectoryId, person.ObjectId, Arg.Is<IReadOnlyCollection<Guid>>(g => g.Contains(secondGroup)), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Jit_PrimeiroTenantNaoTemAPessoa_ProvisionaNoSegundo()
	{
		var person = Person();
		var (firstTenant, firstGroup) = (Guid.CreateVersion7(), Guid.NewGuid());
		var (secondTenant, secondGroup) = (Guid.CreateVersion7(), Guid.NewGuid());
		_rig.AccountStore.Targets.Add(Target(firstTenant, firstGroup));
		_rig.AccountStore.Targets.Add(Target(secondTenant, secondGroup));
		InGroups(person.ObjectId, secondGroup);
		Profile(person);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.Created);
		_rig.Auditor.Records.Single().TenantId.Should().Be(secondTenant);
	}

	[Fact]
	public async Task Jit_ForaDeQualquerGrupo_NaoProvisiona_ERespostaEDefinitiva()
	{
		var person = Person();
		_rig.AccountStore.Targets.Add(Target(_rig.TenantId, Guid.NewGuid()));
		InGroups(person.ObjectId);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.NotProvisioned);
		result.IsDefinitiveRefusal.Should().BeTrue("pode ir ao cache negativo");
		_rig.Events.Should().BeEmpty();
	}

	[Fact]
	public async Task Jit_SemNenhumTenantComGrupoDeProvisionamento_NaoConsultaOGraph()
	{
		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, Guid.NewGuid());

		result.Kind.Should().Be(LoginProvisioningKind.NotProvisioned);
		await _rig.Graph.DidNotReceiveWithAnyArgs().CheckMemberGroupsAsync(default, default, default!, default);
	}

	[Fact]
	public async Task Jit_FalhaDeLeituraDoGraph_Recusa_SemCriar_ENaoEDefinitiva()
	{
		var oid = Guid.NewGuid();
		_rig.AccountStore.Targets.Add(Target(_rig.TenantId, Guid.NewGuid()));
		_rig.GraphChecksFail(oid, SecureGateErrors.Federation.DirectoryUnavailable);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, oid);

		result.Kind.Should().Be(LoginProvisioningKind.Failed);
		result.IsDefinitiveRefusal.Should().BeFalse("o Graph pode voltar no próximo clique");
		_rig.Events.Should().BeEmpty();
	}

	[Fact]
	public async Task Jit_SemConsentimentoDePerfil_Recusa_SemCriar()
	{
		var person = Person();
		var group = Guid.NewGuid();
		_rig.AccountStore.Targets.Add(Target(_rig.TenantId, group));
		InGroups(person.ObjectId, group);
		_rig.Graph.GetPersonAsync(_rig.DirectoryId, person.ObjectId, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result.Failure<DirectoryPerson>(SecureGateErrors.Federation.ConsentRequired)));

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.Failed);
		_rig.Events.Should().NotContain("create");
	}

	[Fact]
	public async Task Jit_DominioForaDaLista_Recusa_ERegistraNaTrilhaComTriggerLogin()
	{
		var person = Person("maria@outra.com");
		var group = Guid.NewGuid();
		_rig.AccountStore.Targets.Add(Target(_rig.TenantId, group));
		InGroups(person.ObjectId, group);
		Profile(person);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.Refused);
		result.IsDefinitiveRefusal.Should().BeTrue();
		_rig.Events.Should().NotContain("create");
		var record = _rig.Auditor.Records.Should().ContainSingle().Subject;
		record.Action.Should().Be(DirectorySyncActions.EmailRefused);
		record.Trigger.Should().Be(DirectorySyncTrigger.Login);
		record.Reason.Should().Be("domain-not-allowed");
	}

	[Fact]
	public async Task Jit_EmailJaEmUso_Recusa_SemCriarSegundaConta()
	{
		var person = Person();
		var group = Guid.NewGuid();
		_rig.AccountStore.Targets.Add(Target(_rig.TenantId, group));
		_rig.AccountStore.EmailsInUse.Add("maria@secco.test");
		InGroups(person.ObjectId, group);
		Profile(person);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.Refused);
		_rig.Events.Should().NotContain("create");
		_rig.Auditor.Records.Should().ContainSingle().Which.Reason.Should().Be("email-in-use");
	}

	[Fact]
	public async Task Jit_TrilhaQueFalha_NaoCriaAConta()
	{
		var person = Person();
		var group = Guid.NewGuid();
		_rig.AccountStore.Targets.Add(Target(_rig.TenantId, group));
		InGroups(person.ObjectId, group);
		Profile(person);
		_rig.Auditor.Accept = _ => false;

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.Failed);
		_rig.Events.Should().NotContain("create");
	}

	[Fact]
	public async Task Jit_MapeamentoParaPerfilReservado_NuncaProvisiona()
	{
		var person = Person();
		var group = Guid.NewGuid();
		_rig.AccountStore.Targets.Add(new ProvisioningTarget(_rig.TenantId, group, SecureGatePlatform.OperatorRole, ["secco.test"]));
		InGroups(person.ObjectId, group);
		Profile(person);

		var result = await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		result.Kind.Should().Be(LoginProvisioningKind.NotProvisioned);
		_rig.Events.Should().NotContain("create");
		await _rig.Graph.DidNotReceiveWithAnyArgs().CheckMemberGroupsAsync(default, default, default!, default);
	}

	[Fact]
	public async Task Jit_CriaNoEscopoDoTenantDoGrupo_NuncaEmModoPlataforma()
	{
		var person = Person();
		var group = Guid.NewGuid();
		var tenant = Guid.CreateVersion7();
		_rig.AccountStore.Targets.Add(Target(tenant, group));
		InGroups(person.ObjectId, group);

		var seen = new List<(Secco.SecureGate.Application.Tenancy.TenantAccessMode Mode, Guid? TenantId)>();
		_rig.Graph.GetPersonAsync(_rig.DirectoryId, person.ObjectId, Arg.Any<CancellationToken>()).Returns(_ =>
		{
			seen.Add((_rig.Scope.Mode, _rig.Scope.TenantId));

			return Task.FromResult(Result.Success(person));
		});

		await _rig.Provisioner().ProvisionOnLoginAsync(_rig.DirectoryId, person.ObjectId);

		seen.Should().ContainSingle().Which.Should().Be((Secco.SecureGate.Application.Tenancy.TenantAccessMode.Tenant, (Guid?)tenant));
	}
}
