using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Orquestração da leitura de grupos do diretório federado (issue #27, ADR-0036): federação
/// ausente/desabilitada responde erro claro, nunca lista vazia; termo de busca inválido não chega
/// ao Graph; tamanho de página é normalizado antes de repassar.
/// </summary>
public class ListEntraGroupsHandlerTests
{
	private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
	private readonly IEntraGroupDirectory _directory = Substitute.For<IEntraGroupDirectory>();
	private readonly ListEntraGroupsHandler _handler;

	public ListEntraGroupsHandlerTests() => _handler = new ListEntraGroupsHandler(_tenants, _directory);

	[Fact]
	public async Task HandleAsync_SemFederacaoCadastrada_RetornaNotEnabled()
	{
		_tenants.GetFederationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TenantFederation?)null);

		var result = await _handler.HandleAsync(Guid.NewGuid(), nameFilter: null, pageToken: null, pageSize: 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.NotEnabled);
		await _directory.DidNotReceiveWithAnyArgs().ListGroupsAsync(default, default, default, default);
	}

	[Fact]
	public async Task HandleAsync_ComFederacaoDesabilitada_RetornaNotEnabled()
	{
		var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());
		federation.SetEnabled(false);
		_tenants.GetFederationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(federation);

		var result = await _handler.HandleAsync(federation.TenantId, nameFilter: null, pageToken: null, pageSize: 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.NotEnabled);
		await _directory.DidNotReceiveWithAnyArgs().ListGroupsAsync(default, default, default, default);
	}

	[Fact]
	public async Task HandleAsync_ComTermoDeBuscaComCaractereDeControle_NaoChegaAoDiretorio()
	{
		var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());
		_tenants.GetFederationAsync(federation.TenantId, Arg.Any<CancellationToken>()).Returns(federation);

		// Controle no MEIO do termo: nas bordas, Trim() já removeria \r\n antes da checagem de
		// controle sequer rodar — o que provaria só o Trim, não a sanitização.
		var result = await _handler.HandleAsync(federation.TenantId, "Financeiro\r\nOutro", pageToken: null, pageSize: 20);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.SearchTermInvalid);
		await _directory.DidNotReceiveWithAnyArgs().ListGroupsAsync(default, default, default, default);
	}

	[Theory]
	[InlineData(0, ListEntraGroupsHandler.DefaultPageSize)]
	[InlineData(-5, ListEntraGroupsHandler.DefaultPageSize)]
	[InlineData(1000, ListEntraGroupsHandler.MaxPageSize)]
	[InlineData(50, 50)]
	public async Task HandleAsync_NormalizaOTamanhoDaPagina(int requested, int expected)
	{
		var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());
		_tenants.GetFederationAsync(federation.TenantId, Arg.Any<CancellationToken>()).Returns(federation);
		_directory.ListGroupsAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success(new EntraGroupPage([], null)));

		await _handler.HandleAsync(federation.TenantId, nameFilter: null, pageToken: null, requested);

		await _directory.Received(1).ListGroupsAsync(
			federation.DirectoryId, Arg.Any<string?>(), Arg.Any<string?>(), expected, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_ComFederacaoHabilitada_RepassaODirectoryIdESanitizaOFiltro()
	{
		var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());
		_tenants.GetFederationAsync(federation.TenantId, Arg.Any<CancellationToken>()).Returns(federation);

		var page = new EntraGroupPage([new EntraGroupDto("g1", "Financeiro", null)], "token-da-proxima");
		_directory.ListGroupsAsync(federation.DirectoryId, "Financ''eiro", "anterior", 20, Arg.Any<CancellationToken>())
			.Returns(Result.Success(page));

		var result = await _handler.HandleAsync(federation.TenantId, "  Financ'eiro  ", "anterior", 20);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeSameAs(page);
	}
}
