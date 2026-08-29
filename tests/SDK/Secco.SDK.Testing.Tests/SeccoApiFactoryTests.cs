using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SDK.Testing;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.Testing.Tests;

/// <summary>Alvo genérico da factory — nenhum teste aqui sobe host, então basta ser uma classe.</summary>
public sealed class FakeProgram;

/// <summary>Expõe os membros protegidos da base para exercitá-los sem subir a aplicação.</summary>
public sealed class TestApiFactory : SeccoApiFactory<FakeProgram>
{
	protected override string Audience => "secco-testes";

	protected override Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;

	public static void CallAddTenant(IDictionary<string, string?> settings, Guid tenantId, string connectionString) =>
		AddTenant(settings, tenantId, connectionString);

	public static void CallAddRolePermissions(
		IDictionary<string, string?> settings,
		string role,
		params string[] permissions) =>
		AddRolePermissions(settings, role, permissions);
}

/// <summary>
/// Geração de token, chave de assinatura e helpers de configuração da base (ADR-0027).
/// Nenhum teste sobe host nem container.
/// </summary>
public class SeccoApiFactoryTests
{
	[Fact]
	public void SigningKey_AcrossInstances_IsDifferent()
	{
		// O teste que trava a regressão: uma chave constante num pacote publicado seria de
		// conhecimento público, e o validador de autenticação não tem como saber disso (ADR-0020).
		using var first = new TestApiFactory();
		using var second = new TestApiFactory();

		second.SigningKey.Should().NotBe(first.SigningKey);
	}

	[Fact]
	public void SigningKey_Always_IsLongEnoughForTheAuthenticationValidator()
	{
		using var factory = new TestApiFactory();

		// O validador da plataforma exige no mínimo 32 caracteres na chave de desenvolvimento.
		factory.SigningKey.Length.Should().BeGreaterThanOrEqualTo(32);
	}

	[Fact]
	public void CreateToken_Always_CarriesShortTenantAndRoleClaims()
	{
		using var factory = new TestApiFactory();
		var tenantId = Guid.NewGuid();

		var token = new JsonWebTokenHandler().ReadJsonWebToken(
			factory.CreateToken(tenantId, subject: "alguem", role: "operador"));

		token.Issuer.Should().Be(SeccoApiFactory<FakeProgram>.TestIssuer);
		token.Audiences.Should().ContainSingle().Which.Should().Be("secco-testes");
		token.GetClaim(SeccoClaims.Subject).Value.Should().Be("alguem");
		token.GetClaim(SeccoClaims.TenantId).Value.Should().Be(tenantId.ToString());
		token.GetClaim(SeccoClaims.Role).Value.Should().Be("operador");
	}

	[Fact]
	public void CreateToken_WithoutExplicitRole_UsesTheDefaultTestRole()
	{
		using var factory = new TestApiFactory();

		var token = new JsonWebTokenHandler().ReadJsonWebToken(factory.CreateToken(Guid.NewGuid()));

		token.GetClaim(SeccoClaims.Role).Value.Should().Be(SeccoApiFactory<FakeProgram>.DefaultTestRole);
	}

	[Fact]
	public void CreateTokenWithScopes_Always_CarriesSpaceSeparatedScopesAndNoTenant()
	{
		using var factory = new TestApiFactory();

		var token = new JsonWebTokenHandler().ReadJsonWebToken(
			factory.CreateTokenWithScopes("securegate:admin", "catalog:logstream"));

		token.GetClaim(SeccoClaims.Scope).Value.Should().Be("securegate:admin catalog:logstream");
		token.TryGetClaim(SeccoClaims.TenantId, out _).Should().BeFalse();
	}

	[Fact]
	public void AddTenant_Always_WritesTheTenancyCatalogKey()
	{
		var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
		var tenantId = Guid.NewGuid();

		TestApiFactory.CallAddTenant(settings, tenantId, "Server=x;Database=y");

		settings[$"Secco:Tenancy:Tenants:{tenantId}:ConnectionString"].Should().Be("Server=x;Database=y");
	}

	[Fact]
	public void AddRolePermissions_Always_EmitsSequentialIndexes()
	{
		var settings = new Dictionary<string, string?>(StringComparer.Ordinal);

		TestApiFactory.CallAddRolePermissions(settings, "test-admin", "a:read", "a:write");

		settings["Secco:Authorization:Roles:test-admin:Permissions:0"].Should().Be("a:read");
		settings["Secco:Authorization:Roles:test-admin:Permissions:1"].Should().Be("a:write");
		settings.Should().HaveCount(2);
	}

	[Fact]
	public void AddRolePermissions_WithMoreThanTenPermissions_KeepsIndexesDistinct()
	{
		var settings = new Dictionary<string, string?>(StringComparer.Ordinal);
		var permissions = Enumerable.Range(0, 12).Select(i => $"recurso{i}:read").ToArray();

		TestApiFactory.CallAddRolePermissions(settings, "test-admin", permissions);

		// Índices além de 9 são o caso que a digitação manual erra na prática.
		settings.Should().HaveCount(12);
		settings["Secco:Authorization:Roles:test-admin:Permissions:10"].Should().Be("recurso10:read");
		settings["Secco:Authorization:Roles:test-admin:Permissions:11"].Should().Be("recurso11:read");
	}
}
