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
}
