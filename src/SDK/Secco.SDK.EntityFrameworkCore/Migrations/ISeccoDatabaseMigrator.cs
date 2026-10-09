namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// Aplica as migrations de um produto (ADR-0038): o banco de plataforma, ou todos os tenants do
/// catálogo. Um alvo que falha não interrompe os demais.
/// </summary>
public interface ISeccoDatabaseMigrator
{
	/// <summary>Nome para log (ex.: "LogStream (tenants)"). Nunca contém connection string.</summary>
	string Name { get; }

	/// <summary>Aplica as migrations. Devolve os alvos que falharam (id do tenant ou nome do banco); vazio = sucesso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default);
}
