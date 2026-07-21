using FluentAssertions;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Invariantes da <see cref="TenantFederation"/> (ADR-0026): o vínculo tenant → diretório
/// Entra é obrigatório e 1:1; a federação nasce habilitada.
/// </summary>
public class TenantFederationTests
{
    [Fact]
    public void Constructor_WithEmptyTenant_Throws()
    {
        var act = () => new TenantFederation(Guid.Empty, Guid.NewGuid());

        act.Should().Throw<DomainInvariantException>();
    }

    [Fact]
    public void Constructor_WithEmptyDirectory_Throws()
    {
        var act = () => new TenantFederation(Guid.NewGuid(), Guid.Empty);

        act.Should().Throw<DomainInvariantException>();
    }

    [Fact]
    public void Constructor_WithValidArguments_CreatesEnabledEntraFederation()
    {
        var tenantId = Guid.NewGuid();
        var directoryId = Guid.NewGuid();

        var federation = new TenantFederation(tenantId, directoryId);

        federation.TenantId.Should().Be(tenantId);
        federation.DirectoryId.Should().Be(directoryId);
        federation.Provider.Should().Be(TenantFederation.EntraProvider);
        federation.IsEnabled.Should().BeTrue("a federação nasce habilitada");
        federation.UpdatedAt.Should().Be(federation.CreatedAt);
    }

    [Fact]
    public void UpdateDirectory_WithEmptyDirectory_Throws()
    {
        var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());

        var act = () => federation.UpdateDirectory(Guid.Empty);

        act.Should().Throw<DomainInvariantException>();
    }

    [Fact]
    public void UpdateDirectory_WithValidDirectory_ReplacesAndTouchesUpdatedAt()
    {
        var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());
        var newDirectory = Guid.NewGuid();

        federation.UpdateDirectory(newDirectory);

        federation.DirectoryId.Should().Be(newDirectory);
        federation.UpdatedAt.Should().BeOnOrAfter(federation.CreatedAt);
    }

    [Fact]
    public void SetEnabled_WithFalse_DisablesFederation()
    {
        var federation = new TenantFederation(Guid.NewGuid(), Guid.NewGuid());

        federation.SetEnabled(false);

        federation.IsEnabled.Should().BeFalse("desabilitar bloqueia o login federado sem apagar o cadastro");
    }
}
