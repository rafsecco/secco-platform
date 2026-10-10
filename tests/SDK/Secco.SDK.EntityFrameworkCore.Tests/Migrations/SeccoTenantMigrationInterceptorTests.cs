using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Migrations;

public class SeccoTenantMigrationInterceptorTests
{
	private const string ConnectionString = "Server=db;Database=tenant_a;User Id=app;Password=segredo";

	private sealed class TenantContext(DbContextOptions<TenantContext> options) : DbContext(options);

	private static ConnectionEventData EventData(SqlConnection connection, DbContext? context) =>
		new(null!, null!, connection, context, Guid.NewGuid(), async: true, DateTimeOffset.UtcNow);

	private static SeccoTenantMigrationInterceptor<TenantContext> Interceptor(SeccoTenantMigrationGate gate) =>
		new(gate, null!, (_, _) => throw new InvalidOperationException("migracao nao deveria ser criada"));

	[Fact]
	public async Task ConnectionOpening_SemContexto_NaoChamaOGate_MesmoComStringNaConexao()
	{
		var gate = new SeccoTenantMigrationGate();
		using var connection = new SqlConnection(ConnectionString);
		var interceptor = Interceptor(gate);

		await interceptor.ConnectionOpeningAsync(connection, EventData(connection, null), default);
		interceptor.ConnectionOpening(connection, EventData(connection, null), default);

		gate.Keys.Should().BeEmpty("sem a string do contexto o interceptor não age (a da conexão pode vir sem senha)");
	}

	[Fact]
	public async Task ConnectionOpening_ContextoSemConnectionString_NaoChamaOGate()
	{
		var gate = new SeccoTenantMigrationGate();
		using var connection = new SqlConnection(ConnectionString);
		await using var context = new TenantContext(new DbContextOptionsBuilder<TenantContext>().UseSqlServer(new SqlConnection()).Options);

		await Interceptor(gate).ConnectionOpeningAsync(connection, EventData(connection, context), default);

		gate.Keys.Should().BeEmpty();
	}
}
