using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Versão de sessão e efeito da revogação (ADR-0032), sobre a API compartilhada (HS256).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class SessionRevocationEffectTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"sessao-{Guid.NewGuid():N}@secco.test";

	private HttpClient ResolverClient()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.AuthorizationRead));

		return client;
	}

	private Task<JsonElement> GetVersionAsync(string subject) =>
		ResolverClient().GetFromJsonAsync<JsonElement>($"/api/v1/authorization/users/{subject}/session-version", Json);

	private async Task<string> StampVersionAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();
		var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());

		return SessionVersion.From(user!.SecurityStamp);
	}

	[Fact]
	public async Task Versao_UsuarioAtivo_DevolveAVersaoAtual()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var version = await GetVersionAsync(userId.ToString());

		version.GetProperty("revoked").GetBoolean().Should().BeFalse();
		version.GetProperty("sessionVersion").GetString().Should().Be(await StampVersionAsync(userId));
	}

	[Fact]
	public async Task Versao_UsuarioDesativado_Revogado()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.DeactivateAsync(factory, userId);

		(await GetVersionAsync(userId.ToString())).GetProperty("revoked").GetBoolean().Should().BeTrue();
	}

	[Fact]
	public async Task Versao_UsuarioBloqueadoPorTentativas_Revogado()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.LockOutAsync(factory, userId, DateTimeOffset.UtcNow.AddMinutes(5));

		(await GetVersionAsync(userId.ToString())).GetProperty("revoked").GetBoolean().Should().BeTrue();
	}

	[Fact]
	public async Task Versao_TenantInativo_Revogado()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			(await context.Tenants.SingleAsync(t => t.Id == _tenantId)).Deactivate();
			await context.SaveChangesAsync();
		}

		(await GetVersionAsync(userId.ToString())).GetProperty("revoked").GetBoolean().Should().BeTrue();
	}

	[Theory]
	[InlineData("nao-e-guid")]
	[InlineData("0192e0a0-0000-7000-8000-000000000000")]
	public async Task Versao_SubjectInexistenteOuInvalido_RevogadoSemDistinguir(string subject)
	{
		var version = await GetVersionAsync(subject);

		version.GetProperty("revoked").GetBoolean().Should().BeTrue();
		version.GetProperty("sessionVersion").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task Versao_SemToken_401() =>
		(await factory.CreateClient().GetAsync($"/api/v1/authorization/users/{Guid.CreateVersion7()}/session-version"))
			.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

	[Fact]
	public async Task Versao_SemScopeAuthorizationRead_403() =>
		(await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/authorization/users/{Guid.CreateVersion7()}/session-version"))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);

	private string RevokeUrl(Guid userId) => $"/api/v1/tenants/{_tenantId}/users/{userId}/sessions/revoke";

	[Fact]
	public async Task RevokeUserSessions_TrocaAVersao()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var before = await StampVersionAsync(userId);

		(await IdentitySeed.AdminClient(factory).PostAsync(RevokeUrl(userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await StampVersionAsync(userId)).Should().NotBe(before);
	}

	[Fact]
	public async Task RevokeUserSessions_UsuarioDeOutroTenant_404ENaoTroca()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		var stranger = await IdentitySeed.UserAsync(factory, otherTenant, Email());
		var before = await StampVersionAsync(stranger);

		(await IdentitySeed.AdminClient(factory).PostAsync(RevokeUrl(stranger), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

		(await StampVersionAsync(stranger)).Should().Be(before);
	}

	[Fact]
	public async Task RevokeUserSessions_SemToken_401_SemScope_403()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		(await factory.CreateClient().PostAsync(RevokeUrl(userId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await ResolverClient().PostAsync(RevokeUrl(userId), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Desativar_TrocaAVersao()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var before = await StampVersionAsync(userId);

		(await IdentitySeed.AdminClient(factory).PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/deactivate", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await StampVersionAsync(userId)).Should().NotBe(before);
	}

	[Fact]
	public async Task RemoverPerfil_Efetivo_TrocaAVersao_Repetido_NaoTroca()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email(), "leitor");
		var admin = IdentitySeed.AdminClient(factory);
		var url = $"/api/v1/tenants/{_tenantId}/users/{userId}/roles/leitor";
		var before = await StampVersionAsync(userId);

		(await admin.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		var afterRemoval = await StampVersionAsync(userId);
		(await admin.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		afterRemoval.Should().NotBe(before);
		(await StampVersionAsync(userId)).Should().Be(afterRemoval, "remoção repetida não pode derrubar a sessão à toa");
	}
}
