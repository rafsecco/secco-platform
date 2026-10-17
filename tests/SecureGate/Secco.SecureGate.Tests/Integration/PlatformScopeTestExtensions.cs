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

		// Row-Level Security (ADR-0041, camada 2): o banco só mostra o que o contexto de sessão
		// declara, então o contexto de fora de DI também leva o interceptor que o grava — o mesmo
		// do runtime. Sem ele este observador, até como dono do banco, enxergaria zero linhas.
		var withSession = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SecureGateDbContext>(options)
			.AddInterceptors(new Secco.SecureGate.Infrastructure.Tenancy.TenantSessionContextInterceptor(
				scope, new Secco.SecureGate.Infrastructure.SecureGateDatabaseOptions()))
			.Options;

		return new SecureGateDbContext(withSession, accessScope: scope);
	}
}

internal static class PlatformRawConnection
{
	/// <summary>
	/// Conexão crua para o teste montar ou conferir dado por SQL (ADR-0041, camada 2): o Row-Level
	/// Security vale também para o dono e o sysadmin, então até o observador de fora precisa declarar
	/// o contexto de plataforma — da mesma forma que o <c>TenantSessionContextInterceptor</c> faz no app.
	/// </summary>
	public static async Task<Microsoft.Data.SqlClient.SqlConnection> OpenPlatformAsync(string connectionString)
	{
		var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
		await connection.OpenAsync();

		await using var command = connection.CreateCommand();
		command.CommandText = "EXEC sp_set_session_context @key = N'secco_mode', @value = N'platform', @read_only = 1;";
		await command.ExecuteNonQueryAsync();

		return connection;
	}
}
