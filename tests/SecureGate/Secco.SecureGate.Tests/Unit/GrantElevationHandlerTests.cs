using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Elevation;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Elevation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Upsert idempotente da concessão de elevação (ADR-0031): cria quando não existe, renova
/// quando existe; usuário fora do tenant da rota é recusado com a mesma resposta de usuário
/// inexistente (ADR-0020) — o teste de resposta HTTP idêntica vive na suíte de integração.
/// </summary>
public class GrantElevationHandlerTests
{
	private readonly IUserDirectory _userDirectory = Substitute.For<IUserDirectory>();
	private readonly IElevationGrantRepository _repository = Substitute.For<IElevationGrantRepository>();
	private readonly GrantElevationHandler _handler;

	public GrantElevationHandlerTests() => _handler = new GrantElevationHandler(_userDirectory, _repository);

	private void SetUpUserInTenant(Guid tenantId, Guid userId) =>
		_userDirectory.BelongsToTenantAsync(tenantId, userId, Arg.Any<CancellationToken>())
			.Returns(true);

	[Fact]
	public async Task HandleAsync_WithoutGrantedBy_ReturnsValidationFailure()
	{
		var result = await _handler.HandleAsync(
			new GrantElevationCommand(Guid.NewGuid(), Guid.NewGuid(), string.Empty, ExpiresAt: null));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Elevation.GrantedByRequired);
	}

	[Fact]
	public async Task HandleAsync_WithExpiresAtInPast_ReturnsValidationFailure()
	{
		var result = await _handler.HandleAsync(new GrantElevationCommand(
			Guid.NewGuid(), Guid.NewGuid(), "admin-sub", DateTimeOffset.UtcNow.AddMinutes(-1)));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Elevation.ExpiresAtInPast);
	}

	[Fact]
	public async Task HandleAsync_WhenUserDoesNotBelongToTenant_ReturnsUserNotFound()
	{
		var tenantId = Guid.NewGuid();
		_userDirectory.BelongsToTenantAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(false);

		var result = await _handler.HandleAsync(
			new GrantElevationCommand(tenantId, Guid.NewGuid(), "admin-sub", ExpiresAt: null));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Elevation.UserNotFound);
	}

	[Fact]
	public async Task HandleAsync_WhenGrantDoesNotExist_CreatesIt()
	{
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		SetUpUserInTenant(tenantId, userId);
		_repository.GetByUserAsync(userId, Arg.Any<CancellationToken>()).Returns((ElevationGrant?)null);

		var result = await _handler.HandleAsync(
			new GrantElevationCommand(tenantId, userId, "admin-sub", ExpiresAt: null));

		result.IsSuccess.Should().BeTrue();
		await _repository.Received(1).UpsertAsync(
			Arg.Is<ElevationGrant>(g => g.UserId == userId && g.TenantId == tenantId && g.GrantedBy == "admin-sub"),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_WhenGrantExists_RenewsTheSameInstance()
	{
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		SetUpUserInTenant(tenantId, userId);
		var existing = new ElevationGrant(userId, tenantId, "original-sub", expiresAt: null, DateTimeOffset.UtcNow);
		_repository.GetByUserAsync(userId, Arg.Any<CancellationToken>()).Returns(existing);
		var newExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

		var result = await _handler.HandleAsync(
			new GrantElevationCommand(tenantId, userId, "renewer-sub", newExpiresAt));

		result.IsSuccess.Should().BeTrue();
		existing.GrantedBy.Should().Be("renewer-sub");
		existing.ExpiresAt.Should().Be(newExpiresAt);
		await _repository.Received(1).UpsertAsync(existing, Arg.Any<CancellationToken>());
	}
}
