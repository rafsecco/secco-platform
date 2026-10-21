using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Operador da instalação por designação (ADR-0049), ponta a ponta com o login real: a pessoa designada na
/// configuração troca o próprio token por um token de operador curto, sem tenant e sem refresh — só se acabou de se
/// autenticar com segundo fator —, cada requisição reconfere a designação, e cada escrita vai à trilha antes.
/// </summary>
[Collection(TenantAdminApiCollectionDefinition.Name)]
public class DesignatedOperatorTests(TenantAdminSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "designated-operator-webapp";
	private const string RedirectUri = "https://localhost/callback";
	private const string LoginScope = "openid offline_access logstream";
	private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
	private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
	private const string MachineClientId = "designated-operator-machine";
	private const string MachineSecret = "designated-operator-machine-secret!!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, IdentitySeed.Password);

	// O "tenant da Intranet": dono da conta designada
	private Guid _ownerTenant;
	private Guid _person;
	private string _personEmail = string.Empty;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await CreateClientWithTokenExchangeAsync();
		await secureGate.CreateClientAsync(MachineClientId, MachineSecret, SecureGateScopes.Admin);
		secureGate.Auditor.Reset();
		secureGate.Designations.Reset();
		secureGate.DesignationClock.Offset = TimeSpan.Zero;

		_ownerTenant = await IdentitySeed.TenantAsync(secureGate);
		_personEmail = $"designada-{Guid.NewGuid():N}@secco.test";
		_person = await IdentitySeed.UserAsync(secureGate, _ownerTenant, _personEmail);
		await IdentitySeed.EnableTwoFactorAsync(secureGate, _person);
	}

	public Task DisposeAsync()
	{
		secureGate.Auditor.Reset();
		secureGate.Designations.Reset();
		secureGate.DesignationClock.Offset = TimeSpan.Zero;

		return Task.CompletedTask;
	}

	// ---- O caso que funciona ----

	[Fact]
	public async Task Designada_RecemAutenticadaComSegundoFator_RecebeTokenDeOperadorEstreito()
	{
		secureGate.Designations.Designate(_person);
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);

		var (status, operatorToken, body) = await ExchangeAsync(accessToken);

		status.Should().Be(HttpStatusCode.OK, body);
		using var payload = JsonDocument.Parse(body);
		payload.RootElement.TryGetProperty("refresh_token", out _).Should().BeFalse("o poder de operador é um ato, sem renovação");

		var jwt = new JsonWebTokenHandler().ReadJsonWebToken(operatorToken!);
		OidcLoginDriver.Scopes(operatorToken!).Should().Equal(SecureGateScopes.Admin);
		jwt.Claims.Should().NotContain(c => c.Type == "tenant_id", "o operador é tenant-less (ADR-0024)");
		jwt.Claims.Should().NotContain(c => c.Type == "role");
		jwt.GetClaim("sub").Value.Should().Be(_person.ToString());
		(jwt.ValidTo - jwt.IssuedAt).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));

		secureGate.Auditor.Records.Should().ContainSingle(r => r.Operation == "DesignatedOperatorTokenIssued")
			.Which.TenantId.Should().Be(_ownerTenant, "a troca vai à trilha do tenant dono da pessoa");
	}

	[Fact]
	public async Task TokenDeOperador_AbreRotaDeOperador_EAuditaAEscrita()
	{
		secureGate.Designations.Designate(_person);
		using var operador = await OperatorTokenClientAsync();
		secureGate.Auditor.Reset();

		(await operador.GetAsync("/api/v1/tenants")).StatusCode.Should().Be(HttpStatusCode.OK);
		secureGate.Auditor.Records.Should().BeEmpty("leitura não vai à trilha");

		var created = await operador.PostAsJsonAsync("/api/v1/tenants", new { name = "Pela Intranet", slug = $"t-{Guid.NewGuid():N}" });

		created.StatusCode.Should().Be(HttpStatusCode.Created);
		var record = secureGate.Auditor.Records.Should().ContainSingle().Subject;
		record.Operation.Should().Be("CreateTenant");
		record.UserId.Should().Be(_person);
		record.Authority.Should().Be(SecureGateScopes.Admin);
		record.ClientId.Should().Be(ClientId, "a aplicação é o act");
	}

	// ---- Quando não troca ----

	[Fact]
	public async Task NaoDesignada_NaoTroca()
	{
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task SemSegundoFator_NaoTroca()
	{
		var email = $"designada-sem-fator-{Guid.NewGuid():N}@secco.test";
		var semFator = await IdentitySeed.UserAsync(secureGate, _ownerTenant, email);
		secureGate.Designations.Designate(semFator);
		var (accessToken, _) = await Driver.LoginAsync(email, LoginScope);

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest, "sem amr=mfa não há poder de instalação");
	}

	[Fact]
	public async Task TokenDeSessaoSemSegundoFator_NaoTroca_MesmoComOFatorCadastradoDepois()
	{
		// A sessão autenticou só com senha; o segundo fator foi cadastrado depois. O token dessa sessão não tem
		// amr=mfa — é exatamente o que um cookie antigo roubado produziria
		var email = $"designada-fator-depois-{Guid.NewGuid():N}@secco.test";
		var pessoa = await IdentitySeed.UserAsync(secureGate, _ownerTenant, email);
		secureGate.Designations.Designate(pessoa);
		var (accessToken, _) = await Driver.LoginAsync(email, LoginScope);

		await IdentitySeed.EnableTwoFactorAsync(secureGate, pessoa);

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task SegundoFatorZeradoDepoisDoLogin_NaoTroca()
	{
		secureGate.Designations.Designate(_person);
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);

		// Zerado direto no cadastro, sem mexer no stamp: isola a exigência do fator cadastrado da conferência de sessão
		using (var scope = secureGate.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var user = await context.Users.SingleAsync(u => u.Id == _person);
			user.TwoFactorEnabled = false;
			await context.SaveChangesAsync();
		}

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task TokenDeVersaoDeSessaoAntiga_NaoTroca()
	{
		secureGate.Designations.Designate(_person);
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);

		// O stamp muda sem passar pela revogação do OpenIddict (é o que a troca de senha e o cadastro do segundo fator
		// fazem): o token de entrada segue válido para o OpenIddict, e só a conferência de sver o recusa (ADR-0032)
		using (var scope = secureGate.Services.CreatePlatformScope())
		{
			var userManager = scope.ServiceProvider
				.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Secco.SecureGate.Infrastructure.Identity.User>>();
			var user = await userManager.FindByIdAsync(_person.ToString());
			(await userManager.UpdateSecurityStampAsync(user!)).Succeeded.Should().BeTrue();
		}

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest, "sessão de versão antiga não vira token de operador");
	}

	[Fact]
	public async Task AutenticacaoAntiga_NaoTroca()
	{
		secureGate.Designations.Designate(_person);
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);

		// O cookie roubado de uma sessão antiga: a janela de autenticação recente já passou
		secureGate.DesignationClock.Offset = TimeSpan.FromMinutes(16);

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task SemAuditoriaConfigurada_NaoTroca()
	{
		secureGate.Designations.Designate(_person);
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);
		secureGate.Auditor.IsConfigured = false;

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task TrilhaRecusadaNaTroca_NaoEmite()
	{
		secureGate.Designations.Designate(_person);
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);
		secureGate.Auditor.Accept = false;

		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task TokenDeOperador_NaoTrocaDeNovo()
	{
		secureGate.Designations.Designate(_person);
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);
		var (_, operatorToken, _) = await ExchangeAsync(accessToken);

		(await ExchangeAsync(operatorToken!)).Status.Should().Be(HttpStatusCode.BadRequest, "o operador não renova o próprio TTL");
	}

	// ---- A cada requisição ----

	[Fact]
	public async Task DesignacaoRetirada_ValeNaRequisicaoSeguinte()
	{
		secureGate.Designations.Designate(_person);
		using var operador = await OperatorTokenClientAsync();
		(await operador.GetAsync("/api/v1/tenants")).StatusCode.Should().Be(HttpStatusCode.OK);

		secureGate.Designations.Revoke(_person);

		(await operador.GetAsync("/api/v1/tenants")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task EscritaComTrilhaRecusada_Responde503_ENaoAcontece()
	{
		secureGate.Designations.Designate(_person);
		using var operador = await OperatorTokenClientAsync();
		secureGate.Auditor.Accept = false;
		var slug = $"t-{Guid.NewGuid():N}";

		var response = await operador.PostAsJsonAsync("/api/v1/tenants", new { name = "Não auditado", slug });

		response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

		secureGate.Auditor.Accept = true;
		using var maquina = await MachineOperatorAsync();
		var tenants = await maquina.GetFromJsonAsync<JsonElement>("/api/v1/tenants", Json);
		tenants.EnumerateArray().Should().NotContain(t => t.GetProperty("slug").GetString() == slug);
	}

	// ---- Proteção da conta designada ----

	[Fact]
	public async Task ContaDesignada_EContaAdministrativa_ParaOAdminDoTenantDono()
	{
		secureGate.Designations.Designate(_person);

		var adminEmail = $"admin-do-dono-{Guid.NewGuid():N}@secco.test";
		var adminDoDono = await IdentitySeed.UserAsync(secureGate, await IdentitySeed.TenantAsync(secureGate), adminEmail);
		await IdentitySeed.EnableTwoFactorAsync(secureGate, adminDoDono);

		using (var maquina = await MachineOperatorAsync())
		{
			(await maquina.PutAsync($"/api/v1/tenants/{_ownerTenant}/admins/{adminDoDono}", null))
				.StatusCode.Should().Be(HttpStatusCode.NoContent);
		}

		await secureGate.CreatePublicClientAsync("designated-ta-client", RedirectUri, SecureGateScopes.TenantAdmin);
		var (adminToken, _) = await new OidcLoginDriver(secureGate, "designated-ta-client", RedirectUri, IdentitySeed.Password)
			.LoginAsync(adminEmail, "openid offline_access securegate:tenant-admin");

		using var admin = Driver.BearerClient(adminToken);

		(await admin.PostAsync($"/api/v1/tenants/{_ownerTenant}/users/{_person}/two-factor/reset", null))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "quem administra o tenant dono não toma a conta que opera a instalação");
	}

	// ---- auth_time, prompt=login, max_age ----

	[Fact]
	public async Task Renovacao_PreservaOAuthTimeDaSessao()
	{
		var (accessToken, refreshToken) = await Driver.LoginAsync(_personEmail, LoginScope);
		var original = AuthTimeOf(accessToken);

		await Task.Delay(TimeSpan.FromSeconds(1.2));
		var refreshed = await Driver.RefreshAsync(refreshToken);
		using var payload = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());

		original.Should().NotBeNull();
		AuthTimeOf(payload.RootElement.GetProperty("access_token").GetString()!)
			.Should().Be(original, "renovar não é autenticar de novo");
	}

	[Theory]
	[InlineData("prompt", "login")]
	[InlineData("max_age", "0")]
	public async Task Authorize_ComPedidoDeReautenticacao_VoltaAoLogin(string parameter, string value)
	{
		using var browser = Driver.CreateBrowser();
		var (verifier, code) = await Driver.ObtainCodeAsync(browser, _personEmail, LoginScope);
		_ = (verifier, code);

		// Mesma sessão, agora pedindo reautenticação: em vez do code, o desafio de login
		await Task.Delay(TimeSpan.FromSeconds(1.2));
		var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = ClientId,
			["redirect_uri"] = RedirectUri,
			["scope"] = LoginScope,
			["code_challenge"] = OidcLoginDriver.CreatePkce().Challenge,
			["code_challenge_method"] = "S256",
			["state"] = "x",
			["nonce"] = "y",
			[parameter] = value,
		});

		var response = await browser.GetAsync(url);

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().Should().Contain("/login", "a sessão antiga não serve: autentica de novo");
		response.Headers.Location!.ToString().Should().NotContain("code=");
	}

	// ---- Apoio ----

	private static long? AuthTimeOf(string accessToken) =>
		new JsonWebTokenHandler().ReadJsonWebToken(accessToken).TryGetPayloadValue<long>("auth_time", out var value) ? value : null;

	private async Task<HttpClient> OperatorTokenClientAsync()
	{
		var (accessToken, _) = await Driver.LoginAsync(_personEmail, LoginScope);
		var (status, operatorToken, body) = await ExchangeAsync(accessToken);
		status.Should().Be(HttpStatusCode.OK, body);

		return Driver.BearerClient(operatorToken!);
	}

	private async Task<(HttpStatusCode Status, string? AccessToken, string Body)> ExchangeAsync(string subjectToken)
	{
		using var client = secureGate.CreateClient();
		using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = TokenExchangeGrant,
			["client_id"] = ClientId,
			["subject_token"] = subjectToken,
			["subject_token_type"] = AccessTokenType,
			["scope"] = SecureGateScopes.Admin,
		}));

		var body = await response.Content.ReadAsStringAsync();
		using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);

		return (response.StatusCode,
			document.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() : null,
			body);
	}

	private async Task<HttpClient> MachineOperatorAsync()
	{
		using var client = secureGate.CreateClient();
		var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = MachineClientId,
			["client_secret"] = MachineSecret,
			["scope"] = SecureGateScopes.Admin,
		}));
		response.EnsureSuccessStatusCode();

		using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		var operador = secureGate.CreateClient();
		operador.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", payload.RootElement.GetProperty("access_token").GetString());

		return operador;
	}

	private async Task CreateClientWithTokenExchangeAsync()
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

		if (await applications.FindByClientIdAsync(ClientId) is not null)
		{
			return;
		}

		// O client da Intranet: login da pessoa + troca por token de operador (client de plataforma, ADR-0037)
		await applications.CreateAsync(new OpenIddictApplicationDescriptor
		{
			ClientId = ClientId,
			ClientType = ClientTypes.Public,
			ConsentType = ConsentTypes.Implicit,
			DisplayName = "Client da Intranet (login + troca de operador)",
			RedirectUris = { new Uri(RedirectUri) },
			Permissions =
			{
				Permissions.Endpoints.Authorization,
				Permissions.Endpoints.Token,
				Permissions.GrantTypes.AuthorizationCode,
				Permissions.GrantTypes.RefreshToken,
				Permissions.Prefixes.GrantType + TokenExchangeGrant,
				Permissions.ResponseTypes.Code,
				Permissions.Prefixes.Scope + "logstream",
				Permissions.Prefixes.Scope + SecureGateScopes.Admin,
			},
		});
	}
}
