using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore;
using Secco.SecureGate.Infrastructure.OpenIddict;

namespace Secco.SecureGate.Infrastructure;

/// <summary>Engines suportados (ADR-0018).</summary>
public enum SecureGateDatabaseProvider
{
	/// <summary>Provider padrão da plataforma.</summary>
	SqlServer = 0,

	/// <summary>Segundo provider suportado.</summary>
	PostgreSql = 1,
}

/// <summary>
/// Banco de PLATAFORMA do SecureGate (seção <c>SecureGate:Database</c>): diferente dos
/// produtos de negócio, aqui a connection string é ÚNICA e fixa (ADR-0022 — identidade
/// não é dado de tenant), não resolvida por catálogo.
/// </summary>
public sealed class SecureGateDatabaseOptions
{
	/// <summary>Engine do banco de plataforma (default: SQL Server, ADR-0018).</summary>
	public SecureGateDatabaseProvider Provider { get; set; } = SecureGateDatabaseProvider.SqlServer;

	/// <summary>
	/// Connection string de RUNTIME do banco <c>secco_securegate</c>. Obrigatória. Deve ser de um
	/// login SEM posse do banco (leitura e escrita) — a ADR-0041 recusa subir fora de Development
	/// com um login dono (<see cref="VerifyRuntimeIsNotOwner"/>).
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>
	/// Connection string de MIGRAÇÃO (login dono do banco, ADR-0041): usada só pelo verbo
	/// <c>migrate</c> e pelo startup em Development, para o DDL. Ausente = usa <see cref="ConnectionString"/>
	/// (compatível com instalações anteriores à separação de credenciais).
	/// </summary>
	public string? MigrationConnectionString { get; set; }

	/// <summary>
	/// Fora de Development, recusa subir quando a credencial de runtime é dona do banco (ADR-0041).
	/// Padrão <c>true</c>; em Development só avisa.
	/// </summary>
	public bool VerifyRuntimeIsNotOwner { get; set; } = true;

	/// <summary>Connection string que o DDL deve usar: a de migração, ou a de runtime se ela não existir.</summary>
	public string EffectiveMigrationConnectionString =>
		string.IsNullOrWhiteSpace(MigrationConnectionString) ? ConnectionString! : MigrationConnectionString;
}

/// <summary>
/// Aplicação do provider selecionado (seletor por receita, ADR-0018/ADR-0027), acrescida da
/// única variação real entre os produtos: as entidades OpenIddict.
/// Público: as fábricas de design-time dos assemblies de migrations o utilizam.
/// </summary>
public static class SecureGateDatabaseProviderConfigurator
{
	private static readonly SeccoDatabaseProviderRegistration[] Registrations =
	[
		new(nameof(SecureGateDatabaseProvider.SqlServer),
			(builder, connectionString) => builder.UseSqlServer(connectionString,
				sql => sql.MigrationsAssembly("Secco.SecureGate.Migrations.SqlServer"))),
		new(nameof(SecureGateDatabaseProvider.PostgreSql),
			(builder, connectionString) => builder.UseNpgsql(connectionString,
				npgsql => npgsql.MigrationsAssembly("Secco.SecureGate.Migrations.Postgres"))),
	];

	public static void Configure(
		DbContextOptionsBuilder optionsBuilder,
		SecureGateDatabaseProvider provider,
		string connectionString)
	{
		SeccoDatabaseProviders.Configure(optionsBuilder, provider.ToString(), connectionString, Registrations);

		// Entidades OpenIddict (chave Guid) entram no modelo em TODOS os caminhos de criação
		// do contexto — runtime, migrations e design-time — para o snapshot nunca divergir
		optionsBuilder.UseOpenIddict<OidcApplication, OidcAuthorization, OidcScope, OidcToken, Guid>();
	}

	/// <summary>Cria options do contexto para processos fora do request (migrations, seed).</summary>
	public static DbContextOptions<Contexts.SecureGateDbContext> CreateOptions(
		SecureGateDatabaseProvider provider,
		string connectionString)
	{
		var builder = new DbContextOptionsBuilder<Contexts.SecureGateDbContext>();
		Configure(builder, provider, connectionString);
		return builder.Options;
	}
}
