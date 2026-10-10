using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Permissions;
using Secco.SecureGate.Application.Roles;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// A recusa do catálogo (ADR-0039) vale só para a permissão ACRESCENTADA (novas menos atuais): órfã
/// já gravada passa, e o detalhe do erro lista no máximo 20 nomes, ordenados.
/// </summary>
public class SetRolePermissionsCatalogTests
{
	private static readonly Guid TenantId = Guid.NewGuid();

	private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
	private readonly IPermissionCatalogRepository _catalog = Substitute.For<IPermissionCatalogRepository>();
	private readonly SetRolePermissionsHandler _handler;

	public SetRolePermissionsCatalogTests()
	{
		_roles.TenantExistsAsync(TenantId, Arg.Any<CancellationToken>()).Returns(true);
		_roles.ReplacePermissionsAsync(TenantId, "leitor", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(true);
		_catalog.FindUnknownAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
			.Returns(call => (IReadOnlyList<string>)[.. call.Arg<IEnumerable<string>>().Where(n => !n.StartsWith("conhecida", StringComparison.Ordinal))]);
		_handler = new SetRolePermissionsHandler(_roles, _catalog);
	}

	private void CurrentPermissions(params string[] current) =>
		_roles.GetPermissionsAsync(TenantId, "leitor", Arg.Any<CancellationToken>()).Returns(current);

	private Task<SharedKernel.Results.Result> SetAsync(params string[] permissions) =>
		_handler.HandleAsync(new SetRolePermissionsCommand(TenantId, "leitor", permissions));

	[Fact]
	public async Task HandleAsync_AddedKnownPermission_Succeeds()
	{
		CurrentPermissions();

		var result = await SetAsync("conhecida:read");

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task HandleAsync_AddedUnknownPermission_FailsNamingIt_AndDoesNotPersist()
	{
		CurrentPermissions();

		var result = await SetAsync("pedido:aprovra");

		result.IsFailure.Should().BeTrue();
		result.Error.Code.Should().Be("SecureGate.Role.PermissionsNotInCatalog");
		result.Error.Description.Should().Contain("pedido:aprovra");
		await _roles.DidNotReceiveWithAnyArgs().ReplacePermissionsAsync(default, default!, default!, default);
	}

	[Fact]
	public async Task HandleAsync_AlreadyStoredOrphanPlusNewKnown_Succeeds()
	{
		CurrentPermissions("antiga:read");

		var result = await SetAsync("antiga:read", "conhecida:read");

		result.IsSuccess.Should().BeTrue("a órfã já gravada não é 'acrescentada' (ADR-0039)");
		await _catalog.Received(1).FindUnknownAsync(
			Arg.Is<IEnumerable<string>>(names => names.SequenceEqual(new[] { "conhecida:read" })), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_NothingAdded_DoesNotConsultTheCatalog()
	{
		CurrentPermissions("antiga:read");

		var result = await SetAsync();

		result.IsSuccess.Should().BeTrue("revogar nunca é recusado");
		await _catalog.DidNotReceiveWithAnyArgs().FindUnknownAsync(default!, default);
	}

	[Fact]
	public async Task HandleAsync_TwentyFiveUnknown_ListsTwentyOrdered()
	{
		CurrentPermissions();
		var unknown = Enumerable.Range(0, 25).Select(i => $"x{i:D2}:read").Reverse().ToArray();

		var result = await SetAsync(unknown);

		result.IsFailure.Should().BeTrue();
		var listed = unknown.Order(StringComparer.Ordinal).ToArray();
		result.Error.Description.Should().Contain(string.Join(", ", listed.Take(20)));
		result.Error.Description.Should().NotContain(listed[20]);
	}

	[Fact]
	public async Task HandleAsync_RoleMissing_ReturnsRoleNotFoundBeforeConsultingCatalog()
	{
		_roles.GetPermissionsAsync(TenantId, "leitor", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>?)null);

		var result = await SetAsync("conhecida:read");

		result.Error.Should().Be(SecureGateErrors.Roles.NotFound);
	}
}
