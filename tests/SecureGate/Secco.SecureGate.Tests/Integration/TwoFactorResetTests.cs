using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Reset do segundo fator pelo administrador (entrega D).
/// </summary>
/// <remarks>
/// Zerar não é isentar: a conta volta ao estado "sem 2FA cadastrado", e sendo de operador o
/// próximo login cai no cadastro (ADR-0030). A operação audita e avisa o dono justamente porque é
/// ela que um admin comprometido usaria para contornar o segundo fator de outra pessoa (ADR-0020).
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class TwoFactorResetTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string AdminClientId = "reset-2fa-admin";
	private const string AdminSecret = "reset-2fa-admin-secret-32-chars-min!";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreateClientAsync(AdminClientId, AdminSecret, "securegate:admin");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"reset-2fa-{Guid.NewGuid():N}@secco.test";

	private async Task<HttpClient> AdminAsync()
	{
		using var anonymous = factory.CreateClient();
		var response = await anonymous.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = AdminClientId,
			["client_secret"] = AdminSecret,
			["scope"] = "securegate:admin",
		}));

		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var json = JsonDocument.Parse(body);
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
			"Bearer", json.RootElement.GetProperty("access_token").GetString());

		return client;
	}

	private async Task<(Guid UserId, string Email)> UserWith2FaAsync()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		var enable = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		await setup.StartEnrollmentAsync(userId);
		var user = await userManager.FindByIdAsync(userId.ToString());
		var codes = await enable.HandleAsync(userId, TotpCalculator.Compute((await userManager.GetAuthenticatorKeyAsync(user!))!));

		codes.Should().HaveCount(10);

		return (userId, email);
	}

	private async Task<TwoFactorState> StateAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();

		return (await scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>().GetStateAsync(userId))!;
	}

	private string Route(Guid userId, Guid? tenantId = null) =>
		$"/api/v1/tenants/{tenantId ?? _tenantId}/users/{userId}/two-factor/reset";

	[Fact]
	public async Task Reset_ZeraOCadastroEAvisaODono()
	{
		var (userId, email) = await UserWith2FaAsync();
		using var admin = await AdminAsync();

		var resposta = await admin.PostAsync(Route(userId), null);

		resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var state = await StateAsync(userId);
		state.Enabled.Should().BeFalse();
		// Zerar, não isentar: sem chave, o próximo cadastro começa do zero.
		state.HasAuthenticator.Should().BeFalse();

		factory.Emails.For(email).Last().Subject.Should().Contain("desativado");
	}

	[Fact]
	public async Task Reset_Repetido_ContinuaRespondendo204()
	{
		var (userId, _) = await UserWith2FaAsync();
		using var admin = await AdminAsync();

		await admin.PostAsync(Route(userId), null);

		// ADR-0034: conta sem 2FA percorre o mesmo caminho e responde igual.
		(await admin.PostAsync(Route(userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Reset_DeOutroTenant_Responde404()
	{
		var (userId, _) = await UserWith2FaAsync();
		var outroTenant = await IdentitySeed.TenantAsync(factory);
		using var admin = await AdminAsync();

		var resposta = await admin.PostAsync(Route(userId, outroTenant), null);

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await StateAsync(userId)).Enabled.Should().BeTrue("a rota de outro tenant não pode mexer na conta");
	}

	[Fact]
	public async Task Reset_SemEscopoDeAdmin_NaoPassa()
	{
		var (userId, _) = await UserWith2FaAsync();
		using var semEscopo = factory.CreateClient();

		var resposta = await semEscopo.PostAsync(Route(userId), null);

		resposta.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
		(await StateAsync(userId)).Enabled.Should().BeTrue();
	}

	[Fact]
	public async Task Detalhe_DoUsuario_MostraOEstadoDoSegundoFator()
	{
		var (userId, _) = await UserWith2FaAsync();
		using var admin = await AdminAsync();

		var antes = await admin.GetFromJsonAsync<JsonElement>(
			$"/api/v1/tenants/{_tenantId}/users/{userId}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

		antes.GetProperty("twoFactorEnabled").GetBoolean().Should().BeTrue();

		await admin.PostAsync(Route(userId), null);

		var depois = await admin.GetFromJsonAsync<JsonElement>(
			$"/api/v1/tenants/{_tenantId}/users/{userId}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

		depois.GetProperty("twoFactorEnabled").GetBoolean().Should().BeFalse();
	}
}
