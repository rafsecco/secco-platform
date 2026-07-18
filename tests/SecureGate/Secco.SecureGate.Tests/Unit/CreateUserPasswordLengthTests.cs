using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Teto de tamanho da senha na criação de usuário (ADR-0020): sem limite superior, uma senha
/// muito longa amplifica o custo do hashing PBKDF2 no Identity antes mesmo de a política de
/// senha ser avaliada — vetor de negação de serviço. A rejeição acontece antes de qualquer
/// acesso ao <see cref="IUserDirectory"/> (guarda em memória, mesmo padrão de
/// <c>ReservedRoleNameTests</c>), então o directory é apenas um substituto que nunca deve
/// ser tocado no caminho rejeitado.
/// </summary>
public class CreateUserPasswordLengthTests
{
    private static readonly Guid AnyTenant = Guid.Parse("018f0000-0000-7000-8000-00000000abcd");

    [Fact]
    public async Task CreateUser_ComSenhaAcimaDoLimite_RetornaPasswordTooLong()
    {
        var roleRepository = Substitute.For<IRoleRepository>();
        var userDirectory = Substitute.For<IUserDirectory>();
        var handler = new CreateUserHandler(roleRepository, userDirectory);

        var senhaMuitoLonga = new string('a', 129);

        var result = await handler.HandleAsync(
            new CreateUserCommand(AnyTenant, "usuario@exemplo.com", senhaMuitoLonga, Roles: null));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SecureGateErrors.Users.PasswordTooLong);
        await userDirectory.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task CreateUser_ComSenhaNoLimite_NaoEhBloqueadaPeloTeto()
    {
        var roleRepository = Substitute.For<IRoleRepository>();
        var userDirectory = Substitute.For<IUserDirectory>();
        roleRepository.TenantExistsAsync(AnyTenant, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateUserHandler(roleRepository, userDirectory);

        var senhaNoLimite = new string('a', 128);

        // 128 caracteres é o limite aceito, não o corte — o fluxo deve chegar ao directory
        // (o retorno do substituto não é configurado; o que importa é a chamada ter ocorrido).
        await handler.HandleAsync(new CreateUserCommand(AnyTenant, "usuario@exemplo.com", senhaNoLimite, Roles: null));

        await userDirectory.Received(1).CreateAsync(
            Arg.Is<CreateUserData>(data => data.Password == senhaNoLimite), Arg.Any<CancellationToken>());
    }
}
