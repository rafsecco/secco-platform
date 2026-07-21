using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Upsert idempotente da federação (ADR-0026): cria quando não existe, atualiza quando
/// existe; tenant inexistente é NotFound; directory id é obrigatório.
/// </summary>
public class UpsertTenantFederationHandlerTests
{
    private readonly ITenantRepository _repository = Substitute.For<ITenantRepository>();
    private readonly UpsertTenantFederationHandler _handler;

    public UpsertTenantFederationHandlerTests() => _handler = new UpsertTenantFederationHandler(_repository);

    [Fact]
    public async Task HandleAsync_WithEmptyDirectoryId_ReturnsValidationFailure()
    {
        var result = await _handler.HandleAsync(
            new UpsertTenantFederationCommand(Guid.NewGuid(), Guid.Empty, Enabled: true));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Federation.DirectoryIdRequired);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownTenant_ReturnsNotFound()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Tenant?)null);

        var result = await _handler.HandleAsync(
            new UpsertTenantFederationCommand(Guid.NewGuid(), Guid.NewGuid(), Enabled: true));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Tenants.NotFound);
    }

    [Fact]
    public async Task HandleAsync_WhenFederationDoesNotExist_CreatesIt()
    {
        var tenant = new Tenant("Tenant", "tenant");
        var directoryId = Guid.NewGuid();
        _repository.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repository.GetFederationAsync(tenant.Id, Arg.Any<CancellationToken>())
            .Returns((TenantFederation?)null);

        var result = await _handler.HandleAsync(
            new UpsertTenantFederationCommand(tenant.Id, directoryId, Enabled: false));

        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).AddFederationAsync(
            Arg.Is<TenantFederation>(f =>
                f.TenantId == tenant.Id && f.DirectoryId == directoryId && !f.IsEnabled),
            Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenFederationExists_UpdatesItWithoutAdding()
    {
        var tenant = new Tenant("Tenant", "tenant");
        var existing = new TenantFederation(tenant.Id, Guid.NewGuid());
        var newDirectoryId = Guid.NewGuid();
        _repository.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _repository.GetFederationAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.HandleAsync(
            new UpsertTenantFederationCommand(tenant.Id, newDirectoryId, Enabled: false));

        result.IsSuccess.Should().BeTrue();
        existing.DirectoryId.Should().Be(newDirectoryId);
        existing.IsEnabled.Should().BeFalse();
        await _repository.DidNotReceive().AddFederationAsync(
            Arg.Any<TenantFederation>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
