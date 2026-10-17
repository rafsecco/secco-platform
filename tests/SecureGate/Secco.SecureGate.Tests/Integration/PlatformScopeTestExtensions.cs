using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O teste que monta ou confere dado direto no banco é um observador de FORA do pipeline HTTP — não
/// tem tenant de rota. Sem escopo o banco não mostra nada (ADR-0041, fail-closed), então esse
/// observador entra em modo plataforma na PRÓPRIA scope de DI dele. Não afeta as requisições
/// HTTP: cada requisição tem a sua scope e entra pelo caminho de produção.
/// </summary>
internal static class PlatformScopeTestExtensions
{
	public static IServiceScope CreatePlatformScope(this IServiceProvider services)
	{
		var scope = services.CreateScope();
		scope.ServiceProvider.GetRequiredService<ITenantAccessScope>()
			.EnterPlatform("teste: observador externo ao pipeline HTTP");

		return scope;
	}
}

internal static class PlatformContextTestFactory
{
	/// <summary>
	/// Contexto criado fora de DI para o teste montar ou conferir dado direto no banco: observador de
	/// fora do pipeline HTTP, em modo plataforma (ADR-0041 — sem escopo o banco não mostra nada).
	/// </summary>
	public static SecureGateDbContext Create(Microsoft.EntityFrameworkCore.DbContextOptions<SecureGateDbContext> options)
	{
		var scope = Secco.SecureGate.Infrastructure.Tenancy.TenantAccessScope.Detached();
		scope.EnterPlatform("teste: observador externo ao pipeline HTTP");

		return new SecureGateDbContext(options, accessScope: scope);
	}
}
