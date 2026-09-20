using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Criação de usuário depois da ADR-0033: <b>o comando não carrega senha nenhuma</b>. A conta nasce
/// sem hash e a pessoa define a credencial pelo convite — antes disto havia sempre um intervalo em
/// que a senha era conhecida por quem criou a conta.
/// </summary>
/// <remarks>
/// O teto de 128 caracteres que existia aqui (ADR-0020, contra amplificação do PBKDF2) não sumiu:
/// mudou de lugar junto com a senha, e hoje vive nas telas que a recebem.
/// </remarks>
public class CreateUserInviteTests
{
	private static readonly Guid AnyTenant = Guid.Parse("018f0000-0000-7000-8000-00000000abcd");

	private static (CreateUserHandler Handler, IUserDirectory Directory, ICredentialTokens Tokens, ICredentialMailer Mailer)
		Build(bool tenantExists = true)
	{
		var roleRepository = Substitute.For<IRoleRepository>();
		roleRepository.TenantExistsAsync(AnyTenant, Arg.Any<CancellationToken>()).Returns(tenantExists);

		var directory = Substitute.For<IUserDirectory>();
		var tokens = Substitute.For<ICredentialTokens>();
		var mailer = Substitute.For<ICredentialMailer>();
		var auditor = Substitute.For<ICredentialAuditor>();

		return (
			new CreateUserHandler(roleRepository, directory, tokens, new InviteUserHandler(tokens, mailer, auditor)),
			directory,
			tokens,
			mailer);
	}

	private static void ReturnsCreated(IUserDirectory directory, ICredentialTokens tokens, Guid userId, bool localLogin)
	{
		directory.CreateAsync(Arg.Any<CreateUserData>(), Arg.Any<CancellationToken>())
			.Returns(new UserDto(userId, "usuario@exemplo.com", AnyTenant, [], UserStatuses.Active));
		tokens.FindAsync(userId, Arg.Any<CancellationToken>())
			.Returns(new CredentialAccount(userId, AnyTenant, "usuario@exemplo.com", HasPassword: false, localLogin, localLogin));
	}

	[Fact]
	public async Task CreateUser_ComLoginLocal_CriaSemSenhaEEnviaConvite()
	{
		var (handler, directory, tokens, mailer) = Build();
		var userId = Guid.CreateVersion7();
		ReturnsCreated(directory, tokens, userId, localLogin: true);

		var result = await handler.HandleAsync(new CreateUserCommand(AnyTenant, "usuario@exemplo.com", LocalLogin: true, Roles: null));

		result.IsSuccess.Should().BeTrue();
		await directory.Received(1).CreateAsync(
			Arg.Is<CreateUserData>(data => data.LocalLogin), Arg.Any<CancellationToken>());
		await mailer.Received(1).SendInviteAsync("usuario@exemplo.com", userId, Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task CreateUser_SemLoginLocal_NaoEnviaConvite()
	{
		var (handler, directory, tokens, mailer) = Build();
		var userId = Guid.CreateVersion7();
		ReturnsCreated(directory, tokens, userId, localLogin: false);

		await handler.HandleAsync(new CreateUserCommand(AnyTenant, "usuario@exemplo.com", LocalLogin: false, Roles: null));

		await directory.Received(1).CreateAsync(
			Arg.Is<CreateUserData>(data => !data.LocalLogin), Arg.Any<CancellationToken>());
		await mailer.DidNotReceiveWithAnyArgs().SendInviteAsync(default!, default, default!, default);
	}

	[Fact]
	public async Task CreateUser_EmailInvalido_NaoTocaNoDirectoryNemNoEnvio()
	{
		var (handler, directory, _, mailer) = Build();

		var result = await handler.HandleAsync(new CreateUserCommand(AnyTenant, "nao-e-email", LocalLogin: true, Roles: null));

		result.IsFailure.Should().BeTrue();
		await directory.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
		await mailer.DidNotReceiveWithAnyArgs().SendInviteAsync(default!, default, default!, default);
	}

	[Fact]
	public async Task CreateUser_TenantInexistente_NaoEnviaConvite()
	{
		var (handler, _, _, mailer) = Build(tenantExists: false);

		var result = await handler.HandleAsync(new CreateUserCommand(AnyTenant, "usuario@exemplo.com", LocalLogin: true, Roles: null));

		result.IsFailure.Should().BeTrue();
		await mailer.DidNotReceiveWithAnyArgs().SendInviteAsync(default!, default, default!, default);
	}
}
