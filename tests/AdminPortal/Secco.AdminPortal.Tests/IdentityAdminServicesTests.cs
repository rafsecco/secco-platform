using FluentAssertions;
using NSubstitute;
using Secco.AdminPortal.Services;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Serviços de gestão de identidade (Fase 7.2): orquestram o <c>Secco.SecureGate.Client</c>
/// e projetam os DTOs gerados para os modelos da UI. Testados com o client substituído
/// (a fábrica devolve o mock) — a autenticação já é coberta em <c>SecureGateClientFactoryTests</c>.
/// </summary>
public class IdentityAdminServicesTests
{
    private static (ISecureGateClientFactory Factory, ISecureGateClient Client) BuildFactory()
    {
        var client = Substitute.For<ISecureGateClient>();
        var factory = Substitute.For<ISecureGateClientFactory>();
        factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);

        return (factory, client);
    }

    [Fact]
    public async Task ListUsers_ProjectsDtoToSummary()
    {
        var (factory, client) = BuildFactory();
        var tenantId = Guid.NewGuid();
        client.ListUsersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<UserDto>
        {
            new() { Id = Guid.NewGuid(), Email = "ana@acme.test", TenantId = tenantId, Roles = ["leitor", "operador"] },
        });

        var users = await new SecureGateUserAdminService(factory).ListUsersAsync(tenantId);

        users.Should().ContainSingle();
        users[0].Email.Should().Be("ana@acme.test");
        users[0].Roles.Should().BeEquivalentTo("leitor", "operador");
    }

    [Fact]
    public async Task CreateUser_SendsEmailPasswordAndRoles()
    {
        var (factory, client) = BuildFactory();
        var tenantId = Guid.NewGuid();

        await new SecureGateUserAdminService(factory)
            .CreateUserAsync(tenantId, "novo@acme.test", "S3nha@Forte!", ["leitor"]);

        await client.Received(1).CreateUserAsync(
            tenantId,
            Arg.Is<CreateUserRequest>(r => r.Email == "novo@acme.test" && r.Password == "S3nha@Forte!" && r.Roles.Contains("leitor")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListRoles_ProjectsPermissions()
    {
        var (factory, client) = BuildFactory();
        var tenantId = Guid.NewGuid();
        client.ListRolesAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<RoleDto>
        {
            new() { Name = "operador", Permissions = ["log-entries:read", "log-entries:write"] },
        });

        var roles = await new SecureGateRoleAdminService(factory).ListRolesAsync(tenantId);

        roles.Should().ContainSingle();
        roles[0].Name.Should().Be("operador");
        roles[0].Permissions.Should().BeEquivalentTo("log-entries:read", "log-entries:write");
    }

    [Fact]
    public async Task SetPermissions_SendsTheFullSet()
    {
        var (factory, client) = BuildFactory();
        var tenantId = Guid.NewGuid();

        await new SecureGateRoleAdminService(factory)
            .SetPermissionsAsync(tenantId, "operador", ["log-entries:read", "api-call-logs:read"]);

        await client.Received(1).SetRolePermissionsAsync(
            tenantId,
            "operador",
            Arg.Is<SetRolePermissionsRequest>(r => r.Permissions.Count == 2 && r.Permissions.Contains("api-call-logs:read")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertDatabase_SendsConnectionStringWriteOnly()
    {
        var (factory, client) = BuildFactory();
        var tenantId = Guid.NewGuid();

        await new SecureGateTenantAdminService(factory)
            .UpsertDatabaseAsync(tenantId, "logstream", "Server=segredo;Database=x;");

        await client.Received(1).UpsertTenantDatabaseAsync(
            tenantId,
            "logstream",
            Arg.Is<UpsertTenantDatabaseRequest>(r => r.ConnectionString == "Server=segredo;Database=x;"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTenant_ProjectsDetail()
    {
        var (factory, client) = BuildFactory();
        var id = Guid.NewGuid();
        client.GetTenantAsync(id, Arg.Any<CancellationToken>()).Returns(new TenantDetailDto
        {
            Id = id, Name = "Acme", Slug = "acme", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, Products = ["logstream"],
        });

        var detail = await new SecureGateTenantAdminService(factory).GetTenantAsync(id);

        detail.Name.Should().Be("Acme");
        detail.IsActive.Should().BeTrue();
        detail.Products.Should().ContainSingle().Which.Should().Be("logstream");
    }
}
