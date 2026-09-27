namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Sanitiza o filtro de busca por nome antes de entrar no <c>$filter</c> OData da consulta ao
/// Graph (ADR-0020/0036).
/// </summary>
/// <remarks>
/// OData monta a expressão por concatenação de texto, exatamente como SQL dinâmico — aspa simples
/// não escapada fecha a string do filtro e o resto vira sintaxe OData controlada por quem digitou
/// a busca. A defesa é a mesma da injeção de SQL: nunca aceitar aspa simples sem dobrar, e nunca
/// aceitar caractere de controle (quebra de linha não tem por que existir num nome de busca).
/// </remarks>
public static class EntraGroupNameFilter
{
	/// <summary>Tamanho máximo aceito para o termo de busca.</summary>
	public const int MaxLength = 100;

	/// <summary>
	/// Normaliza e escapa o termo de busca. <c>null</c>/vazio devolve <c>null</c> (sem filtro).
	/// </summary>
	/// <param name="input">Termo cru, possivelmente <c>null</c>.</param>
	/// <exception cref="ArgumentException">Termo acima do limite ou com caractere de controle.</exception>
	public static string? Sanitize(string? input)
	{
		var trimmed = input?.Trim();

		if (string.IsNullOrEmpty(trimmed))
		{
			return null;
		}

		if (trimmed.Length > MaxLength)
		{
			throw new ArgumentException($"O termo de busca excede o limite de {MaxLength} caracteres.", nameof(input));
		}

		foreach (var character in trimmed)
		{
			if (char.IsControl(character))
			{
				throw new ArgumentException("O termo de busca não pode conter caractere de controle.", nameof(input));
			}
		}

		// Aspa simples é o delimitador de string do OData: dobrar é o escape padrão do protocolo
		// (equivalente ao "duplicar aspa" do SQL-92), não uma remoção de caractere.
		return trimmed.Replace("'", "''", StringComparison.Ordinal);
	}
}
