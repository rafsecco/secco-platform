using System.Text.RegularExpressions;

namespace Secco.SharedKernel.Authorization;

/// <summary>
/// Formato canônico de permissão da plataforma (ADR-0021): <c>recurso:acao</c> em
/// kebab-case minúsculo (ex.: <c>log-entries:read</c>, <c>invoices:write</c>) —
/// namespaced por recurso para evitar colisão semântica entre produtos. As CONSTANTES
/// de permissão vivem em cada produto (regra de admissão da ADR-0003); o kernel
/// fornece apenas a composição e a validação do formato, usadas por todos.
/// </summary>
public static partial class SeccoPermissions
{
	/// <summary>Tamanho máximo aceito para uma permissão completa.</summary>
	public const int MaxLength = 100;

	/// <summary>Separador entre recurso e ação.</summary>
	public const char Separator = ':';

	[GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*:[a-z0-9]+(?:-[a-z0-9]+)*$")]
	private static partial Regex PermissionFormat();

	[GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*-\\{[a-z]+\\}:[a-z0-9]+(?:-[a-z0-9]+)*$")]
	private static partial Regex FamilyFormat();

	/// <summary>Verifica se a string está no formato canônico <c>recurso:acao</c>.</summary>
	/// <param name="permission">Permissão candidata.</param>
	public static bool IsValid(string? permission) =>
		!string.IsNullOrEmpty(permission)
		&& permission.Length <= MaxLength
		&& PermissionFormat().IsMatch(permission);

	/// <summary>Compõe uma permissão validando o formato das partes.</summary>
	/// <param name="resource">Recurso (kebab-case minúsculo, ex.: <c>log-entries</c>).</param>
	/// <param name="action">Ação (kebab-case minúsculo, ex.: <c>read</c>).</param>
	/// <exception cref="ArgumentException">Se o resultado não estiver no formato canônico.</exception>
	public static string Create(string resource, string action)
	{
		var permission = $"{resource}{Separator}{action}";

		return IsValid(permission)
			? permission
			: throw new ArgumentException(
				$"'{permission}' não está no formato canônico de permissão 'recurso:acao' " +
				$"(kebab-case minúsculo, máximo de {MaxLength} caracteres).");
	}

	/// <summary>
	/// Verifica se a string é uma família de permissão (ADR-0043): <c>&lt;prefixo&gt;-{&lt;param&gt;}:&lt;acao&gt;</c>
	/// (ex.: <c>setor-{id}:read</c>) — UM placeholder <c>[a-z]+</c> só no fim do segmento de recurso,
	/// prefixo literal kebab-case obrigatório e ação fixa. Família NÃO é um nome de permissão válido
	/// (<see cref="IsValid"/> a recusa). A regex valida apenas a DECLARAÇÃO (padrão fixo e linear).
	/// </summary>
	/// <param name="family">Família candidata.</param>
	public static bool IsValidFamily(string? family) =>
		!string.IsNullOrEmpty(family)
		&& family.Length <= MaxLength
		&& FamilyFormat().IsMatch(family);

	/// <summary>
	/// Verifica se a permissão canônica pertence à família (ADR-0043): começa com o prefixo literal
	/// (até o <c>-</c> antes do placeholder), termina com <c>:&lt;acao&gt;</c> e sobra ao menos um caractere
	/// entre os dois, sem <c>:</c>. Comparação ordinal, SEM expressão regular sobre o input.
	/// </summary>
	/// <param name="family">Família declarada (<see cref="IsValidFamily"/>).</param>
	/// <param name="permission">Permissão candidata (<see cref="IsValid"/>).</param>
	public static bool MatchesFamily(string family, string permission)
	{
		if (!IsValidFamily(family) || !IsValid(permission))
		{
			return false;
		}

		SplitFamily(family, out var prefix, out var suffix);

		return permission.Length > prefix.Length + suffix.Length
			&& permission.StartsWith(prefix, StringComparison.Ordinal)
			&& permission.EndsWith(suffix, StringComparison.Ordinal)
			&& permission.AsSpan(prefix.Length, permission.Length - prefix.Length - suffix.Length).IndexOf(Separator) < 0;
	}

	/// <summary>Substitui o placeholder da família pelo valor da instância, validando o resultado.</summary>
	/// <param name="family">Família declarada (ex.: <c>setor-{id}:read</c>).</param>
	/// <param name="value">Valor da instância em kebab-case minúsculo (ex.: <c>rh</c>).</param>
	/// <exception cref="ArgumentException">Se a família for inválida ou o resultado não casar com ela.</exception>
	public static string FromFamily(string family, string value)
	{
		if (!IsValidFamily(family))
		{
			throw new ArgumentException($"'{family}' não é uma família de permissão válida (ADR-0043).", nameof(family));
		}

		SplitFamily(family, out var prefix, out var suffix);

		var permission = prefix + value + suffix;

		return MatchesFamily(family, permission)
			? permission
			: throw new ArgumentException(
				$"'{permission}' não é uma permissão válida da família '{family}' " +
				$"(kebab-case minúsculo, máximo de {MaxLength} caracteres).",
				nameof(value));
	}

	// Família já validada: prefixo = até o '-' antes de '{' (inclusive); sufixo = de ':' (inclusive) em diante
	private static void SplitFamily(string family, out string prefix, out string suffix)
	{
		prefix = family[..family.IndexOf('{')];
		suffix = family[family.LastIndexOf(Separator)..];
	}
}
