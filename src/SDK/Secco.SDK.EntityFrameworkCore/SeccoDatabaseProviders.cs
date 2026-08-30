using Microsoft.EntityFrameworkCore;

namespace Secco.SDK.EntityFrameworkCore;

/// <summary>
/// Receita de aplicação de um provider de banco a um <see cref="DbContextOptionsBuilder"/>
/// (ADR-0018). O produto declara o nome do provider e a ação que o configura — inclusive o
/// assembly de migrations, que fica dentro de <see cref="Apply"/> para o SDK nunca precisar
/// conhecer o pacote de migrations do produto nem o engine por trás do nome.
/// </summary>
/// <param name="Name">
/// Nome do provider, comparado contra o valor recebido em <see cref="SeccoDatabaseProviders"/>
/// (tipicamente <c>Provider.ToString()</c> do enum do próprio produto — ordinal, ignore-case).
/// </param>
/// <param name="Apply">Ação que configura o builder recebendo a connection string.</param>
public sealed record SeccoDatabaseProviderRegistration(
	string Name,
	Action<DbContextOptionsBuilder, string> Apply);

/// <summary>
/// Seletor de provider de banco por receita (ADR-0018, ADR-0027). O
/// <c>Secco.SDK.EntityFrameworkCore</c> permanece agnóstico de engine — esta classe nunca chama
/// <c>UseSqlServer</c>/<c>UseNpgsql</c> diretamente — e cada produto declara as receitas que
/// aceita através de <see cref="SeccoDatabaseProviderRegistration"/>. Cada produto mantém o
/// próprio enum de provider (API legítima, com validação de configuração tipada de graça) e
/// alimenta este seletor com <c>Provider.ToString()</c>; o seletor é a rede secundária de
/// fail-fast, não a validação primária.
/// </summary>
public static class SeccoDatabaseProviders
{
	/// <summary>
	/// Aplica ao <paramref name="builder"/> a receita cujo nome corresponde a
	/// <paramref name="providerName"/> (comparação ordinal ignore-case).
	/// </summary>
	/// <param name="builder">Options builder do contexto a configurar.</param>
	/// <param name="providerName">
	/// Nome do provider, tipicamente <c>Provider.ToString()</c> do enum do produto chamador.
	/// </param>
	/// <param name="connectionString">Connection string repassada à receita selecionada.</param>
	/// <param name="registrations">Receitas aceitas pelo produto chamador.</param>
	/// <exception cref="ArgumentException">
	/// Nenhuma receita corresponde a <paramref name="providerName"/>. A mensagem cita o valor
	/// recebido e a lista de nomes aceitos (ADR-0020) — nunca a connection string, que não entra
	/// nesta assinatura de exceção.
	/// </exception>
	public static void Configure(
		DbContextOptionsBuilder builder,
		string providerName,
		string connectionString,
		params SeccoDatabaseProviderRegistration[] registrations)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(providerName);
		ArgumentNullException.ThrowIfNull(connectionString);
		ArgumentNullException.ThrowIfNull(registrations);

		var registration = registrations.FirstOrDefault(
			candidate => string.Equals(candidate.Name, providerName, StringComparison.OrdinalIgnoreCase));

		if (registration is null)
		{
			var accepted = string.Join(", ", registrations.Select(candidate => candidate.Name));
			throw new ArgumentException(
				$"Provider de banco desconhecido: '{providerName}'. Aceitos: {accepted}.",
				nameof(providerName));
		}

		registration.Apply(builder, connectionString);
	}

	/// <summary>
	/// Cria <see cref="DbContextOptions{TContext}"/> para processos fora do request (migrations,
	/// seed, workers), aplicando a receita selecionada por <paramref name="providerName"/>.
	/// </summary>
	/// <typeparam name="TContext">Tipo do <see cref="DbContext"/> do produto.</typeparam>
	/// <param name="providerName">
	/// Nome do provider, tipicamente <c>Provider.ToString()</c> do enum do produto chamador.
	/// </param>
	/// <param name="connectionString">Connection string repassada à receita selecionada.</param>
	/// <param name="registrations">Receitas aceitas pelo produto chamador.</param>
	/// <exception cref="ArgumentException">
	/// Nenhuma receita corresponde a <paramref name="providerName"/> — ver <see cref="Configure"/>.
	/// </exception>
	public static DbContextOptions<TContext> CreateOptions<TContext>(
		string providerName,
		string connectionString,
		params SeccoDatabaseProviderRegistration[] registrations)
		where TContext : DbContext
	{
		var builder = new DbContextOptionsBuilder<TContext>();
		Configure(builder, providerName, connectionString, registrations);
		return builder.Options;
	}
}
