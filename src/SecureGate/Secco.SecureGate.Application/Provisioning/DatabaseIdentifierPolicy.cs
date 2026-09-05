using System.Text.RegularExpressions;

namespace Secco.SecureGate.Application.Provisioning;

/// <summary>
/// Validação e delimitação de identificadores de banco (nome de database, de login e de usuário).
/// </summary>
/// <remarks>
/// Esta classe é a barreira de injeção desta entrega, e existe porque <b>identificador não pode
/// ser parametrizado em DDL</b>: não há <c>@parametro</c> em <c>CREATE DATABASE</c> nem em
/// <c>CREATE LOGIN</c>, então o nome entra no comando por concatenação — e sem allowlist isso é
/// injeção de SQL com privilégio de administrador do servidor (ADR-0020).
/// <para>
/// A postura é allowlist estrita, nunca blocklist: só passa o que casa com o formato, e o que
/// passa ainda é delimitado. É a mesma disciplina que o <c>SeccoSqlServerInstance</c> do
/// <c>Secco.SDK.Testing</c> já aplica antes de <c>CREATE</c>/<c>DROP DATABASE</c>.
/// </para>
/// </remarks>
public static partial class DatabaseIdentifierPolicy
{
	/// <summary>Tamanho máximo aceito para um identificador.</summary>
	public const int MaxLength = 63;

	/// <summary>Tamanho mínimo aceito para um identificador.</summary>
	public const int MinLength = 3;

	/// <summary>
	/// Nomes reservados de sistema, recusados mesmo casando com o formato — provisionar por cima
	/// de um banco de sistema é acidente grave e irreversível.
	/// </summary>
	private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
	{
		"master", "model", "msdb", "tempdb", "postgres", "template0", "template1", "sys", "information_schema",
	};

	/// <summary>
	/// Indica se o identificador é aceitável: minúsculas, dígitos e sublinhado, começando por
	/// letra, dentro do tamanho e fora da lista de nomes reservados.
	/// </summary>
	/// <param name="identifier">Identificador candidato.</param>
	public static bool IsValid(string? identifier) =>
		!string.IsNullOrEmpty(identifier)
		&& identifier.Length >= MinLength
		&& identifier.Length <= MaxLength
		&& SafeIdentifier().IsMatch(identifier)
		&& !Reserved.Contains(identifier);

	/// <summary>
	/// Delimita um identificador já validado para uso em DDL do SQL Server, escapando o
	/// fechamento de colchete.
	/// </summary>
	/// <param name="identifier">Identificador previamente aprovado por <see cref="IsValid"/>.</param>
	/// <exception cref="ArgumentException">Se o identificador não tiver passado pela validação.</exception>
	public static string QuoteSqlServer(string identifier)
	{
		// Defesa em profundidade: mesmo sendo chamada depois do IsValid, esta função nunca
		// delimita o que não foi aprovado — um caminho novo que esqueça a validação falha aqui.
		if (!IsValid(identifier))
		{
			throw new ArgumentException("Identificador de banco inválido.", nameof(identifier));
		}

		return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
	}

	/// <summary>
	/// Deriva um identificador a partir de partes livres (slug do tenant, produto), normalizando
	/// para o formato aceito. O resultado ainda precisa passar por <see cref="IsValid"/>.
	/// </summary>
	/// <param name="parts">Partes a concatenar com sublinhado.</param>
	public static string Derive(params string[] parts)
	{
		ArgumentNullException.ThrowIfNull(parts);

		var joined = string.Join('_', parts.Where(part => !string.IsNullOrWhiteSpace(part)))
			.Trim()
			.ToLowerInvariant();

		var normalized = NonIdentifierCharacters().Replace(joined, "_").Trim('_');

		return normalized.Length > MaxLength ? normalized[..MaxLength].TrimEnd('_') : normalized;
	}

	[GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
	private static partial Regex SafeIdentifier();

	[GeneratedRegex("[^a-z0-9_]+", RegexOptions.CultureInvariant)]
	private static partial Regex NonIdentifierCharacters();
}
