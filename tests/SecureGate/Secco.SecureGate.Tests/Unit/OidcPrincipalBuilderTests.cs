using FluentAssertions;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Gate de emissão do token tenant-less (ADR-0020/0023/0024): o tratamento de operador
/// (sem <c>tenant_id</c>) só vale para quem tem o role <c>platform-operator</c> E está no
/// tenant de plataforma. O nome do role é único apenas por tenant — um "platform-operator"
/// forjado num tenant de cliente NÃO pode receber o token tenant-less.
/// </summary>
public class OidcPrincipalBuilderTests
{
    private static readonly Guid CustomerTenant = Guid.Parse("018f0000-0000-7000-8000-00000000c1e7");

    private static User UserIn(Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        UserName = "user@secco.test",
        Email = "user@secco.test",
    };

    private static string? TenantClaim(System.Security.Claims.ClaimsPrincipal principal) =>
        principal.FindFirst(SeccoClaims.TenantId)?.Value;

    [Fact]
    public void ForUser_OperadorNoTenantDePlataforma_NaoRecebeTenantId()
    {
        var user = UserIn(SecureGatePlatform.TenantId);

        var principal = OidcPrincipalBuilder.ForUser(
            user, [SecureGatePlatform.OperatorRole], scopes: [], resources: []);

        TenantClaim(principal).Should().BeNull(
            "o operador de plataforma é tenant-less — escolhe o tenant por requisição (ADR-0024)");
    }

    [Fact]
    public void ForUser_RoleDeOperadorNumTenantDeCliente_AindaRecebeTenantId()
    {
        // Impostor: role LITERALMENTE chamado platform-operator, mas num tenant de cliente
        var user = UserIn(CustomerTenant);

        var principal = OidcPrincipalBuilder.ForUser(
            user, [SecureGatePlatform.OperatorRole], scopes: [], resources: []);

        TenantClaim(principal).Should().Be(CustomerTenant.ToString(),
            "colisão de nome fora do tenant de plataforma não concede o token tenant-less (ADR-0020)");
    }

    [Fact]
    public void ForUser_UsuarioComumNumTenantDeCliente_RecebeTenantId()
    {
        var user = UserIn(CustomerTenant);

        var principal = OidcPrincipalBuilder.ForUser(user, ["auditor"], scopes: [], resources: []);

        TenantClaim(principal).Should().Be(CustomerTenant.ToString(),
            "usuário comum segue com tenant_id (isolamento da ADR-0005 intacto)");
    }
}
