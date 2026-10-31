using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Marcar e desmarcar o grupo de provisionamento (ADR-0055): no máximo um por tenant, as duas operações idempotentes
/// (ADR-0034), e a corrida entre dois pedidos resolvida pelo índice único do banco.
/// </summary>
public class SetProvisioningGroupHandlerTests
{
	private readonly IGroupRoleMappingRepository _mappings = Substitute.For<IGroupRoleMappingRepository>();
	private readonly Guid _tenantId = Guid.NewGuid();

	private SetProvisioningGroupHandler Handler() => new(_mappings);

	private TenantGroupRoleMapping Mapping(bool provisioning = false)
	{
		var mapping = new TenantGroupRoleMapping(_tenantId, Guid.NewGuid(), "Grupo", Guid.NewGuid());

		if (provisioning)
		{
			mapping.MarkAsProvisioningGroup();
		}

		_mappings.GetByIdAsync(_tenantId, mapping.Id, Arg.Any<CancellationToken>()).Returns(mapping);

		return mapping;
	}

	[Fact]
	public async Task Marcar_MapeamentoInexistente_E404()
	{
		_mappings.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TenantGroupRoleMapping?)null);

		var result = await Handler().MarkAsync(_tenantId, Guid.NewGuid());

		result.Error.Should().Be(SecureGateErrors.Federation.MappingNotFound);
	}

	[Fact]
	public async Task Marcar_NenhumOutroMarcado_Marca_ESalva()
	{
		var mapping = Mapping();
		_mappings.TrySaveProvisioningChangeAsync(Arg.Any<CancellationToken>()).Returns(true);

		var result = await Handler().MarkAsync(_tenantId, mapping.Id);

		result.IsSuccess.Should().BeTrue();
		mapping.IsProvisioningGroup.Should().BeTrue();
		await _mappings.Received(1).TrySaveProvisioningChangeAsync(Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Marcar_JaMarcado_EIdempotente_SemSalvar()
	{
		var mapping = Mapping(provisioning: true);

		var result = await Handler().MarkAsync(_tenantId, mapping.Id);

		result.IsSuccess.Should().BeTrue();
		await _mappings.DidNotReceiveWithAnyArgs().TrySaveProvisioningChangeAsync(default);
	}

	[Fact]
	public async Task Marcar_OutroGrupoJaEOProvisionamento_E409_ENaoMarca()
	{
		var current = Mapping(provisioning: true);
		var other = Mapping();
		_mappings.FindProvisioningAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(current);

		var result = await Handler().MarkAsync(_tenantId, other.Id);

		result.Error.Should().Be(SecureGateErrors.Federation.ProvisioningGroupAlreadySet);
		other.IsProvisioningGroup.Should().BeFalse();
		await _mappings.DidNotReceiveWithAnyArgs().TrySaveProvisioningChangeAsync(default);
	}

	[Fact]
	public async Task Marcar_QuandoOBancoBarraOSegundo_Corrida_E409()
	{
		var mapping = Mapping();
		_mappings.TrySaveProvisioningChangeAsync(Arg.Any<CancellationToken>()).Returns(false);

		var result = await Handler().MarkAsync(_tenantId, mapping.Id);

		result.Error.Should().Be(SecureGateErrors.Federation.ProvisioningGroupAlreadySet);
	}

	[Fact]
	public async Task Desmarcar_Marcado_Desmarca_ESalva()
	{
		var mapping = Mapping(provisioning: true);

		var result = await Handler().UnmarkAsync(_tenantId, mapping.Id);

		result.IsSuccess.Should().BeTrue();
		mapping.IsProvisioningGroup.Should().BeFalse();
		await _mappings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Desmarcar_NaoMarcado_EIdempotente_SemSalvar()
	{
		var mapping = Mapping();

		var result = await Handler().UnmarkAsync(_tenantId, mapping.Id);

		result.IsSuccess.Should().BeTrue();
		await _mappings.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	[Fact]
	public async Task Desmarcar_MapeamentoInexistente_E404()
	{
		_mappings.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TenantGroupRoleMapping?)null);

		var result = await Handler().UnmarkAsync(_tenantId, Guid.NewGuid());

		result.Error.Should().Be(SecureGateErrors.Federation.MappingNotFound);
	}
}

/// <summary>Domínios de e-mail permitidos no upsert da federação (ADR-0055): validados, e a ausência mantém a lista.</summary>
public class UpsertTenantFederationDomainsTests
{
	private static readonly string[] SingleDomain = ["empresa.com"];

	private readonly ITenantRepository _repository = Substitute.For<ITenantRepository>();
	private readonly Tenant _tenant = new("Tenant", "tenant");
	private readonly Guid _directory = Guid.NewGuid();

	private UpsertTenantFederationHandler Handler() => new(_repository);

	private TenantFederation WithExisting(params string[] domains)
	{
		var federation = new TenantFederation(_tenant.Id, _directory);
		federation.SetAllowedEmailDomains(domains);
		_repository.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);
		_repository.GetFederationAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(federation);

		return federation;
	}

	[Fact]
	public async Task ListaOmitida_MantemADaFederacaoExistente()
	{
		var federation = WithExisting("empresa.com");

		var result = await Handler().HandleAsync(new UpsertTenantFederationCommand(_tenant.Id, _directory, Enabled: false));

		result.IsSuccess.Should().BeTrue();
		federation.GetAllowedEmailDomains().Should().Equal(["empresa.com"], "só ligar ou desligar a federação não apaga a lista");
	}

	[Fact]
	public async Task ListaVazia_EsvaziaALista()
	{
		var federation = WithExisting("empresa.com");

		var result = await Handler().HandleAsync(new UpsertTenantFederationCommand(_tenant.Id, _directory, true, []));

		result.IsSuccess.Should().BeTrue();
		federation.GetAllowedEmailDomains().Should().BeEmpty();
	}

	[Fact]
	public async Task ListaInformada_SubstituiANormalizada()
	{
		var federation = WithExisting("velha.com");

		var result = await Handler().HandleAsync(
			new UpsertTenantFederationCommand(_tenant.Id, _directory, true, ["Nova.com", "outra.com.br"]));

		result.IsSuccess.Should().BeTrue();
		federation.GetAllowedEmailDomains().Should().Equal("nova.com", "outra.com.br");
	}

	[Theory]
	[InlineData("*.empresa.com")]
	[InlineData("empresa")]
	[InlineData("a@empresa.com")]
	public async Task DominioInvalido_E400_ENadaMuda(string domain)
	{
		var federation = WithExisting("empresa.com");

		var result = await Handler().HandleAsync(new UpsertTenantFederationCommand(_tenant.Id, _directory, true, [domain]));

		result.Error.Should().Be(SecureGateErrors.Federation.AllowedEmailDomainsInvalid);
		federation.GetAllowedEmailDomains().Should().Equal("empresa.com");
		await _repository.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
	}

	[Fact]
	public async Task MaisDominiosQueOTeto_E400()
	{
		WithExisting();
		var tooMany = Enumerable.Range(0, EmailDomainRules.MaxCount + 1).Select(i => $"d{i}.com").ToList();

		var result = await Handler().HandleAsync(new UpsertTenantFederationCommand(_tenant.Id, _directory, true, tooMany));

		result.Error.Should().Be(SecureGateErrors.Federation.AllowedEmailDomainsInvalid);
	}

	[Fact]
	public async Task FederacaoNova_ComLista_NasceComOsDominios()
	{
		_repository.GetByIdAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns(_tenant);
		_repository.GetFederationAsync(_tenant.Id, Arg.Any<CancellationToken>()).Returns((TenantFederation?)null);

		var result = await Handler().HandleAsync(
			new UpsertTenantFederationCommand(_tenant.Id, _directory, true, ["empresa.com"]));

		result.IsSuccess.Should().BeTrue();
		await _repository.Received(1).AddFederationAsync(
			Arg.Is<TenantFederation>(f => f.GetAllowedEmailDomains().SequenceEqual(SingleDomain)),
			Arg.Any<CancellationToken>());
	}
}
