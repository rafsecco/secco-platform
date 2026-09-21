using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Remoção do vínculo com o diretório externo (entrega C).
/// </summary>
/// <remarks>
/// A operação <b>não bloqueia ninguém</b>: enquanto a pessoa seguir no diretório e a federação do
/// tenant estiver ligada, o próximo login casa por e-mail e vincula de novo (ADR-0026). Serve
/// para vínculo morto. A única guarda é não deixar a conta sem caminho de entrada.
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class ExternalLoginUnlinkTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string AdminClientId = "unlink-admin";
	private const string AdminSecret = "unlink-admin-secret-32-chars-minimo!";
	private const string Provider = "EntraId";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreateClientAsync(AdminClientId, AdminSecret, "securegate:admin");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

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

	private async Task<Guid> UserWithLinkAsync(bool localLogin = true)
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, $"unlink-{Guid.NewGuid():N}@secco.test");

		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.FindAsync(userId);
		user!.LocalLoginEnabled = localLogin;
		context.UserLogins.Add(new UserLogin
		{
			UserId = userId,
			LoginProvider = Provider,
			ProviderKey = $"{Guid.NewGuid():D}:{Guid.NewGuid():D}",
			ProviderDisplayName = "Microsoft Entra ID",
		});
		await context.SaveChangesAsync();

		return userId;
	}

	private async Task<IReadOnlyList<string>> LinksAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.UserLogins
			.Where(login => login.UserId == userId)
			.Select(login => login.LoginProvider)
			.ToListAsync();
	}

	private string Route(Guid userId, Guid? tenantId = null) =>
		$"/api/v1/tenants/{tenantId ?? _tenantId}/users/{userId}/external-logins/{Provider}";

	[Fact]
	public async Task Desvincular_RemoveOVinculoEResponde204()
	{
		var userId = await UserWithLinkAsync();
		using var admin = await AdminAsync();

		var response = await admin.DeleteAsync(Route(userId));

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await LinksAsync(userId)).Should().BeEmpty();
	}

	[Fact]
	public async Task Desvincular_Repetido_ContinuaRespondendo204()
	{
		var userId = await UserWithLinkAsync();
		using var admin = await AdminAsync();

		await admin.DeleteAsync(Route(userId));

		// ADR-0034: DELETE é idempotente de fato — o SDK repete esse método sozinho depois de
		// um timeout, e a segunda passagem não pode virar erro.
		(await admin.DeleteAsync(Route(userId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Desvincular_ContaSoCorporativa_Responde409()
	{
		var userId = await UserWithLinkAsync(localLogin: false);
		using var admin = await AdminAsync();

		var response = await admin.DeleteAsync(Route(userId));

		// O vínculo é o único caminho de entrada: removê-lo criaria uma conta órfã — existe,
		// tem perfis, e ninguém entra nela.
		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await LinksAsync(userId)).Should().ContainSingle();
	}

	[Fact]
	public async Task Desvincular_DeOutroTenant_Responde404()
	{
		var userId = await UserWithLinkAsync();
		var outroTenant = await IdentitySeed.TenantAsync(factory);
		using var admin = await AdminAsync();

		var response = await admin.DeleteAsync(Route(userId, outroTenant));

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await LinksAsync(userId)).Should().ContainSingle("nada pode ser removido pela rota de outro tenant");
	}

	[Fact]
	public async Task Desvincular_SemEscopoDeAdmin_NaoPassa()
	{
		var userId = await UserWithLinkAsync();
		using var semEscopo = factory.CreateClient();

		var response = await semEscopo.DeleteAsync(Route(userId));

		response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
		(await LinksAsync(userId)).Should().ContainSingle();
	}

	[Fact]
	public async Task Desvincular_ContaSoCorporativaDeOutroTenant_Responde404ENao409()
	{
		var userId = await UserWithLinkAsync(localLogin: false);
		var outroTenant = await IdentitySeed.TenantAsync(factory);
		using var admin = await AdminAsync();

		var response = await admin.DeleteAsync(Route(userId, outroTenant));

		// 409 aqui contaria que existe, em OUTRO tenant, uma conta com aquele id que entra só
		// pelo diretório. A resposta de tenant errado é sempre a de inexistente (ADR-0020).
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Adaptador_DeOutroTenant_NaoRemoveNada()
	{
		var userId = await UserWithLinkAsync();
		var outroTenant = await IdentitySeed.TenantAsync(factory);

		using var scope = factory.Services.CreateScope();
		var directory = scope.ServiceProvider.GetRequiredService<Application.Users.IUserDirectory>();

		// A guarda de tenant existe em DUAS camadas: o caso de uso recusa antes de chamar, e o
		// adaptador recusa de novo. Sem este teste, remover a segunda passaria despercebido — e
		// ela é a que protege qualquer chamador futuro que não passe pelo caso de uso.
		var removed = await directory.RemoveExternalLoginAsync(outroTenant, userId, Provider);

		removed.Should().BeFalse();
		(await LinksAsync(userId)).Should().ContainSingle();
	}
}
