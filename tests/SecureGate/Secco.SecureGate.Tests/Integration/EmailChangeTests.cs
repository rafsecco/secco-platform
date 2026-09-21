using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Troca do próprio e-mail (entrega C).
/// </summary>
/// <remarks>
/// O e-mail é o username da plataforma (ADR-0022) e, no primeiro login federado, a chave que casa
/// a pessoa com o diretório (ADR-0026). Por isso a troca é confirmada no endereço NOVO e move o
/// <c>SecurityStamp</c>, derrubando sessões e links pendentes (ADR-0032/0033).
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class EmailChangeTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "troca-email-e2e";
	private const string RedirectUri = "https://localhost/callback";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"troca-email-{Guid.NewGuid():N}@secco.test";

	private async Task<(Guid UserId, string Email)> UserAsync()
	{
		var email = Email();

		return (await IdentitySeed.UserAsync(factory, _tenantId, email), email);
	}

	[Fact]
	public async Task Token_DeTrocaDeEmail_SoValeParaOEnderecoQueOGerou()
	{
		var (userId, _) = await UserAsync();
		var destino = Email();
		var outro = Email();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		var token = await tokens.CreateEmailChangeTokenAsync(userId, destino);

		// O endereço novo faz parte do propósito do token: um link interceptado não serve para
		// apontar a conta a outro lugar.
		(await tokens.ChangeEmailAsync(userId, outro, token)).Should().Be(CredentialTokenOutcome.InvalidToken);
		(await tokens.ChangeEmailAsync(userId, destino, token)).Should().Be(CredentialTokenOutcome.Done);
	}

	[Fact]
	public async Task TrocaDeEmail_MudaTambemOUserName()
	{
		var (userId, _) = await UserAsync();
		var destino = Email();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		await tokens.ChangeEmailAsync(userId, destino, await tokens.CreateEmailChangeTokenAsync(userId, destino));

		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.FindAsync(userId);

		user!.Email.Should().Be(destino);
		// E-mail e username são a mesma coisa (ADR-0022): deixar um para trás faria a pessoa
		// continuar logando com o endereço antigo depois de trocá-lo.
		user.UserName.Should().Be(destino);
		user.NormalizedUserName.Should().Be(destino.ToUpperInvariant());
	}

	[Fact]
	public async Task TrocaDeEmail_ParaEnderecoEmUso_NaoAcontece()
	{
		var (userId, original) = await UserAsync();
		var (_, ocupado) = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		var token = await tokens.CreateEmailChangeTokenAsync(userId, ocupado);

		(await tokens.ChangeEmailAsync(userId, ocupado, token)).Should().Be(CredentialTokenOutcome.NotAllowed);

		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.Users.FindAsync(userId))!.Email.Should().Be(original);
	}

	[Fact]
	public async Task EmailJaEmUso_NaoEstaDisponivel()
	{
		var (_, existente) = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();

		(await tokens.IsEmailAvailableAsync(existente)).Should().BeFalse();
		(await tokens.IsEmailAvailableAsync(Email())).Should().BeTrue();
	}

	[Fact]
	public async Task ConferirSenha_SoAceitaASenhaCerta()
	{
		var (userId, _) = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();

		(await tokens.CheckPasswordAsync(userId, IdentitySeed.Password)).Should().BeTrue();
		(await tokens.CheckPasswordAsync(userId, "Errada@Senha1")).Should().BeFalse();
	}
}
