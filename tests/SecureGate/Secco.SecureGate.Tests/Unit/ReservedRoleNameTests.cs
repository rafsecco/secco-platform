using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Roles;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Reserva do nome <c>platform-operator</c> na gestão de roles (ADR-0020/0023/0024): a
/// criação e a substituição de permissões rejeitam esse nome em qualquer tenant — o role
/// legítimo nasce só pelo seed de referência, nunca por API. A rejeição acontece antes de
/// qualquer acesso ao repositório (guarda em memória), então o repositório é apenas um
/// substituto que nunca deve ser tocado no caminho reservado.
/// </summary>
public class ReservedRoleNameTests
{
    private static readonly Guid AnyTenant = Guid.Parse("018f0000-0000-7000-8000-00000000abcd");

    [Fact]
    public async Task CreateRole_ComNomeReservado_RetornaNameReserved()
    {
        var repository = Substitute.For<IRoleRepository>();
        var handler = new CreateRoleHandler(repository);

        var result = await handler.HandleAsync(new CreateRoleCommand(AnyTenant, SecureGatePlatform.OperatorRole));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Roles.NameReserved);
        await repository.DidNotReceiveWithAnyArgs().CreateRoleAsync(default, default!, default);
    }

    [Theory]
    [InlineData("PLATFORM-OPERATOR")]
    [InlineData("Platform-Operator")]
    [InlineData("platform-Operator")]
    public async Task CreateRole_ComVariacaoDeCaixaDoNomeReservado_RetornaNameReserved(string name)
    {
        var repository = Substitute.For<IRoleRepository>();
        var handler = new CreateRoleHandler(repository);

        // O Identity normaliza o nome — quase-variações de caixa também não podem ser criadas
        var result = await handler.HandleAsync(new CreateRoleCommand(AnyTenant, name));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Roles.NameReserved);
        await repository.DidNotReceiveWithAnyArgs().CreateRoleAsync(default, default!, default);
    }

    [Fact]
    public async Task CreateRole_NoTenantDePlataforma_TambemRejeitaNomeReservado()
    {
        var repository = Substitute.For<IRoleRepository>();
        var handler = new CreateRoleHandler(repository);

        // A guarda é incondicional: nem no tenant de plataforma o role é criado via API
        var result = await handler.HandleAsync(
            new CreateRoleCommand(SecureGatePlatform.TenantId, SecureGatePlatform.OperatorRole));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Roles.NameReserved);
    }

    [Fact]
    public async Task SetPermissions_ComNomeReservado_RetornaNameReserved()
    {
        var repository = Substitute.For<IRoleRepository>();
        var handler = new SetRolePermissionsHandler(repository);

        var result = await handler.HandleAsync(new SetRolePermissionsCommand(
            AnyTenant, SecureGatePlatform.OperatorRole, ["log-entries:read"]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Roles.NameReserved);
        await repository.DidNotReceiveWithAnyArgs()
            .ReplacePermissionsAsync(default, default!, default!, default);
    }

    [Theory]
    [InlineData("PLATFORM-OPERATOR")]
    [InlineData("Platform-Operator")]
    public async Task SetPermissions_ComVariacaoDeCaixaDoNomeReservado_RetornaNameReserved(string name)
    {
        var repository = Substitute.For<IRoleRepository>();
        var handler = new SetRolePermissionsHandler(repository);

        var result = await handler.HandleAsync(new SetRolePermissionsCommand(
            AnyTenant, name, ["log-entries:read"]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Roles.NameReserved);
    }

    [Fact]
    public async Task CreateRole_ComNomeComum_NaoEhBloqueadoPelaReserva()
    {
        var repository = Substitute.For<IRoleRepository>();
        repository.TenantExistsAsync(AnyTenant, Arg.Any<CancellationToken>()).Returns(true);
        repository.RoleExistsAsync(AnyTenant, "auditor", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CreateRoleHandler(repository);

        var result = await handler.HandleAsync(new CreateRoleCommand(AnyTenant, "auditor"));

        result.IsSuccess.Should().BeTrue("um nome comum não é reservado");
        await repository.Received(1).CreateRoleAsync(AnyTenant, "auditor", Arg.Any<CancellationToken>());
    }
}
