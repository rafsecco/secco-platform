using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Reconciliação de perfis com o diretório (ADR-0055, entrega A): cada regra tem um teste que quebraria sem ela.
/// Tudo em memória — o que se prova é a decisão (o que muda, em que ordem, o que nunca muda), não o banco nem o Graph.
/// </summary>
public class DirectoryRoleReconcilerTests
{
	private readonly DirectoryReconcilerRig _rig = new();

	private static string[] OnlyAudit(IEnumerable<string> events) =>
		[.. events.Where(e => e.StartsWith("audit:", StringComparison.Ordinal))];

	// ---- Login: acrescentar ----

	[Fact]
	public async Task Login_GrupoMapeado_AcrescentaOPerfilComOrigemDirectoryEGrupo()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.GraphChecks(oid, group);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Added.Should().Be(1);
		_rig.Store.Find(user, "financeiro").Should().BeEquivalentTo(
			new { Origin = RoleAssignmentOrigin.Directory, SourceGroupId = (Guid?)group });
	}

	[Fact]
	public async Task Login_TrilhaVemAntesDaInclusao_EDizAtorDiretorioSemDadoDoPedido()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.GraphChecks(oid, group);

		await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		_rig.Events.Should().ContainInOrder("audit:perfil-acrescentado", "add");
		var record = _rig.Auditor.Records.Should().ContainSingle().Subject;
		record.Should().BeEquivalentTo(new
		{
			_rig.TenantId,
			_rig.DirectoryId,
			Action = DirectorySyncActions.RoleAdded,
			Trigger = DirectorySyncTrigger.Login,
			UserId = (Guid?)user,
			RoleName = "financeiro",
			GroupId = (Guid?)group,
		});
	}

	[Fact]
	public async Task Login_AtribuicaoManualQueOGrupoTambemJustifica_ContinuaManualSemTocar()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Manual);
		_rig.GraphChecks(oid, group);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Added.Should().Be(0);
		_rig.Store.Find(user, "financeiro")!.Origin.Should().Be(RoleAssignmentOrigin.Manual);
		_rig.Events.Should().BeEmpty("nada mudou nem foi registrado");
	}

	[Fact]
	public async Task Login_SemMapeamentoNoTenant_NaoChamaOGraphNemMudaNada()
	{
		var (user, oid) = _rig.Account();

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		await _rig.Graph.DidNotReceiveWithAnyArgs().CheckMemberGroupsAsync(default, default, default!, default);
		_rig.Events.Should().BeEmpty();
	}

	// ---- Login: retirar ----

	[Fact]
	public async Task Login_GrupoNaoJustificaMais_RetiraSoAAtribuicaoDirectory_ERevogaAsSessoesAntes()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Directory, group);
		_rig.Store.Has(user, "outro", RoleAssignmentOrigin.Manual);
		_rig.GraphChecks(oid);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Removed.Should().Be(1);
		_rig.Store.Find(user, "financeiro").Should().BeNull();
		_rig.Store.Find(user, "outro").Should().NotBeNull("a manual nunca é tocada");
		_rig.Revoker.Revoked.Should().ContainSingle().Which.Should().Be((user, SessionRevocationReason.RoleRemoved));
		_rig.Events.Should().ContainInOrder("audit:perfil-retirado", "revoke", "remove");
	}

	[Fact]
	public async Task Login_AtribuicaoManualNuncaESaiMesmoSemGrupo()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Manual);
		_rig.GraphChecks(oid);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Removed.Should().Be(0);
		_rig.Store.Find(user, "financeiro").Should().NotBeNull();
		_rig.Revoker.Revoked.Should().BeEmpty();
	}

	[Fact]
	public async Task Login_AtribuicaoDirectorySemNenhumMapeamentoNoTenant_AindaESaiSemChamarOGraph()
	{
		// O admin removeu o mapeamento: o que ele concedia não pode ficar preso (RemoveUserRole recusa a origem Directory)
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Directory, Guid.NewGuid());

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Removed.Should().Be(1);
		await _rig.Graph.DidNotReceiveWithAnyArgs().CheckMemberGroupsAsync(default, default, default!, default);
	}

	[Fact]
	public async Task Login_OutroGrupoMapeadoAoMesmoPerfilPassaAJustificar_TrocaOGrupoDeOrigemSemRetirar()
	{
		var groupA = Guid.NewGuid();
		var groupB = Guid.NewGuid();
		_rig.Map(groupA, "financeiro");
		_rig.Map(groupB, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Directory, groupA);
		_rig.GraphChecks(oid, groupB);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Removed.Should().Be(0);
		_rig.Store.Find(user, "financeiro")!.SourceGroupId.Should().Be(groupB);
		_rig.Revoker.Revoked.Should().BeEmpty("o perfil continua justificado");
	}

	// ---- Guarda do último membro ativo ----

	[Fact]
	public async Task Retirada_QueDeixariaOPerfilSemMembroAtivo_EBarradaERegistrada()
	{
		var group = Guid.NewGuid();
		var roleId = _rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Directory, group);
		_rig.Store.LastActive.Add((roleId, user));
		_rig.GraphChecks(oid);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Blocked.Should().Be(1);
		result.Removed.Should().Be(0);
		_rig.Store.Find(user, "financeiro").Should().NotBeNull();
		_rig.Revoker.Revoked.Should().BeEmpty("barrada não derruba a sessão de ninguém");
		OnlyAudit(_rig.Events).Should().Equal("audit:retirada-barrada");
	}

	// ---- Fail-closed: trilha ----

	[Fact]
	public async Task Inclusao_ComTrilhaQueFalha_NaoAcontece()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.GraphChecks(oid, group);
		_rig.Auditor.Accept = _ => false;

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Added.Should().Be(0);
		result.Unrecorded.Should().Be(1);
		_rig.Store.Find(user, "financeiro").Should().BeNull();
		_rig.Events.Should().NotContain("add");
	}

	[Fact]
	public async Task Retirada_ComTrilhaQueFalha_NaoRetiraNemRevoga()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Directory, group);
		_rig.GraphChecks(oid);
		_rig.Auditor.Accept = _ => false;

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Removed.Should().Be(0);
		result.Unrecorded.Should().Be(1);
		_rig.Store.Find(user, "financeiro").Should().NotBeNull();
		_rig.Revoker.Revoked.Should().BeEmpty();
	}

	// ---- Fail-closed: Graph ----

	[Fact]
	public async Task Login_ComFalhaDoGraph_NaoMudaNada_ENaoVaiATrilha()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "outro", RoleAssignmentOrigin.Directory, Guid.NewGuid());
		_rig.GraphChecksFail(oid, SecureGateErrors.Federation.DirectoryUnavailable);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Outcome.Should().Be(DirectorySyncOutcome.ReadFailed);
		result.ErrorCode.Should().Be(SecureGateErrors.Federation.DirectoryUnavailable.Code);
		_rig.Store.Find(user, "financeiro").Should().BeNull();
		_rig.Store.Find(user, "outro").Should().NotBeNull("a falha de leitura não retira nada");
		_rig.Events.Should().BeEmpty("a falha do login fica no log de quem chama; só a do ciclo vai à trilha");
		_rig.Auditor.Records.Should().BeEmpty();
		_rig.Revoker.Revoked.Should().BeEmpty();
	}

	[Fact]
	public async Task Ciclo_QualquerGrupoQueFalhaNaLeitura_AbortaOTenantSemMudarNada_NemOsAcrescimosDosOutros()
	{
		var okGroup = Guid.NewGuid();
		var failingGroup = Guid.NewGuid();
		_rig.Map(okGroup, "financeiro");
		_rig.Map(failingGroup, "rh");
		var (user, oid) = _rig.Account();
		_rig.GraphMembers(okGroup, oid);
		_rig.GraphMembersFail(failingGroup, SecureGateErrors.Federation.DirectoryReadIncomplete);

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.ReadFailed);
		_rig.Store.Assignments.Should().BeEmpty("leitura incompleta nunca é aplicada pela metade");
		_rig.Events.Should().Equal("audit:leitura-falhou");
	}

	// ---- Ciclo ----

	[Fact]
	public async Task Ciclo_AcrescentaERetiraPorConta_ENuncaTocaOManual()
	{
		var financeiro = Guid.NewGuid();
		var rh = Guid.NewGuid();
		_rig.Map(financeiro, "financeiro");
		_rig.Map(rh, "rh");
		var (ana, anaOid) = _rig.Account();
		var (bia, biaOid) = _rig.Account();
		var (cris, _) = _rig.Account();

		_rig.GraphMembers(financeiro, anaOid);
		_rig.GraphMembers(rh, anaOid, biaOid);

		_rig.Store.Has(bia, "financeiro", RoleAssignmentOrigin.Directory, financeiro);
		_rig.Store.Has(cris, "rh", RoleAssignmentOrigin.Manual);

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Added.Should().Be(3, "ana ganha financeiro e rh, bia ganha rh");
		result.Removed.Should().Be(1, "bia saiu de financeiro");
		_rig.Store.Find(ana, "financeiro").Should().NotBeNull();
		_rig.Store.Find(ana, "rh").Should().NotBeNull();
		_rig.Store.Find(bia, "financeiro").Should().BeNull();
		_rig.Store.Find(bia, "rh").Should().NotBeNull();
		_rig.Store.Find(cris, "rh")!.Origin.Should().Be(RoleAssignmentOrigin.Manual, "cris não está em grupo algum e a atribuição é manual");
		_rig.Revoker.Revoked.Select(r => r.UserId).Should().Equal(bia);
	}

	[Fact]
	public async Task Ciclo_SoReconciliaContasVinculadasAoDiretorio()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var outsider = Guid.NewGuid();
		_rig.Store.Has(outsider, "financeiro", RoleAssignmentOrigin.Directory, group);
		_rig.GraphMembers(group);

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Removed.Should().Be(0, "conta sem vínculo com o diretório não tem como ser verificada: fica como está");
		_rig.Store.Find(outsider, "financeiro").Should().NotBeNull();
	}

	// ---- Freio de massa ----

	private (List<Guid> Users, Guid Group) PopulateDirectoryAssignments(int holders, int stillMembers)
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var users = new List<Guid>();
		var oids = new List<Guid>();

		for (var i = 0; i < holders; i++)
		{
			var (user, oid) = _rig.Account();
			users.Add(user);
			_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Directory, group);

			if (i < stillMembers)
			{
				oids.Add(oid);
			}
		}

		_rig.GraphMembers(group, [.. oids]);

		return (users, group);
	}

	[Fact]
	public async Task Freio_RetiradasAcimaDaFracaoEDoPiso_AbortaOTenantInteiro_ERegistra()
	{
		PopulateDirectoryAssignments(holders: 20, stillMembers: 14); // 6 saem = 30% e > 5

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.AbortedByBrake);
		_rig.Store.Assignments.Should().HaveCount(20, "nada foi retirado");
		_rig.Revoker.Revoked.Should().BeEmpty();
		_rig.Events.Should().Equal("audit:ciclo-abortado");
		_rig.Auditor.Records.Single().Should().BeEquivalentTo(new { Count = (int?)6, Total = (int?)20 });
	}

	[Fact]
	public async Task Freio_Abortado_NaoAplicaNemAsInclusoesDoMesmoTenant()
	{
		var (_, group) = PopulateDirectoryAssignments(holders: 20, stillMembers: 14);
		var newcomer = _rig.Account();
		_rig.GraphMembers(group, [newcomer.ObjectId, .. _rig.Store.Accounts.Take(14).Select(a => a.ObjectId)]);

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.AbortedByBrake);
		_rig.Store.Find(newcomer.UserId, "financeiro").Should().BeNull("o tenant inteiro fica como estava");
	}

	[Fact]
	public async Task Freio_ExatamenteNoPisoDeCinco_NaoAborta()
	{
		PopulateDirectoryAssignments(holders: 20, stillMembers: 15); // 5 saem: 25% mas NÃO acima de 5

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Removed.Should().Be(5);
	}

	[Fact]
	public async Task Freio_AcimaDoPisoMasAbaixoDaFracao_NaoAborta()
	{
		PopulateDirectoryAssignments(holders: 100, stillMembers: 94); // 6 saem = 6% (abaixo de 10%)

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Removed.Should().Be(6);
	}

	[Fact]
	public async Task Freio_ListaDeMembrosVazia_NaoDerrubaATodos()
	{
		PopulateDirectoryAssignments(holders: 20, stillMembers: 0); // o Graph "devolveu" um grupo vazio

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.AbortedByBrake);
		_rig.Store.Assignments.Should().HaveCount(20);
	}

	[Fact]
	public async Task Freio_UsaOsLimitesConfigurados()
	{
		_rig.Options.MaxRemovalRatio = 0.5;
		_rig.Options.MinRemovalsBeforeBrake = 1;
		PopulateDirectoryAssignments(holders: 20, stillMembers: 14); // 30% < 50%

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Removed.Should().Be(6);
	}

	[Fact]
	public async Task Freio_NaoContaAsRetiradasDeMapeamentoQueOAdminRemoveu()
	{
		// Remover um mapeamento grande é ato do admin, não mudança anômala do diretório: não pode travar o tenant
		var removedGroup = Guid.NewGuid();
		var keptGroup = Guid.NewGuid();
		_rig.Map(keptGroup, "rh");
		var users = Enumerable.Range(0, 10).Select(_ => _rig.Account()).ToList();

		foreach (var (user, _) in users)
		{
			_rig.Store.Has(user, "financeiro", RoleAssignmentOrigin.Directory, removedGroup);
		}

		_rig.GraphMembers(keptGroup);

		var result = await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		result.Outcome.Should().Be(DirectorySyncOutcome.Completed);
		result.Removed.Should().Be(10);
	}

	// ---- Defesas ----

	[Fact]
	public async Task PerfilReservado_NuncaEAtribuido_MesmoComMapeamentoAntigoApontandoParaEle()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, SecureGatePlatform.OperatorRole);
		var (user, oid) = _rig.Account();
		_rig.GraphChecks(oid, group);

		var result = await _rig.Build().ReconcileLoginAsync(_rig.TenantId, _rig.DirectoryId, user, oid);

		result.Added.Should().Be(0);
		_rig.Store.Assignments.Should().BeEmpty();
		await _rig.Graph.DidNotReceiveWithAnyArgs().CheckMemberGroupsAsync(default, default, default!, default);
	}

	[Fact]
	public async Task TenantDePlataforma_NuncaEReconciliado()
	{
		var (user, oid) = _rig.Account();

		var login = await _rig.Build().ReconcileLoginAsync(SecureGatePlatform.TenantId, _rig.DirectoryId, user, oid);
		var cycle = await _rig.Build().ReconcileTenantAsync(SecureGatePlatform.TenantId, _rig.DirectoryId);

		login.Outcome.Should().Be(DirectorySyncOutcome.Skipped);
		cycle.Outcome.Should().Be(DirectorySyncOutcome.Skipped);
		_rig.Store.ScopesSeen.Should().BeEmpty();
		await _rig.Mappings.DidNotReceiveWithAnyArgs().ListAsync(default, default);
	}

	[Fact]
	public async Task TodaChamadaDeDadosRodaNoEscopoDoTenant_NuncaEmModoPlataforma()
	{
		var group = Guid.NewGuid();
		_rig.Map(group, "financeiro");
		var (user, oid) = _rig.Account();
		_rig.Store.Has(user, "rh", RoleAssignmentOrigin.Directory, group);
		_rig.GraphMembers(group, oid);

		await _rig.Build().ReconcileTenantAsync(_rig.TenantId, _rig.DirectoryId);

		_rig.Store.ScopesSeen.Should().NotBeEmpty();
		_rig.Store.ScopesSeen.Should().OnlyContain(s => s.Mode == TenantAccessMode.Tenant && s.TenantId == _rig.TenantId);
		_rig.Scope.Mode.Should().Be(TenantAccessMode.None, "o escopo é restaurado ao fim");
	}
}
