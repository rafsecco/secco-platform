using System.Text.RegularExpressions;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>
/// Regras de formato para identificadores do catálogo (ADR-0020: input externo nunca é
/// propagado sem validação — slug e produto entram em rotas, scopes e nomes derivados).
/// </summary>
internal static partial class TenantInputRules
{
    /// <summary>Kebab-case minúsculo: letras/dígitos separados por hífens simples.</summary>
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();

    /// <summary>Valida um slug de tenant já normalizado (minúsculo).</summary>
    /// <param name="slug">Slug candidato.</param>
    /// <param name="maxLength">Limite de tamanho.</param>
    public static bool IsValidSlug(string slug, int maxLength) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= maxLength && KebabCase().IsMatch(slug);
}
