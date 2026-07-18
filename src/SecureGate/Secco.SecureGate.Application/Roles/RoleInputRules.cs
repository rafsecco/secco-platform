using System.Text.RegularExpressions;

namespace Secco.SecureGate.Application.Roles;

/// <summary>
/// Regras de formato para nomes de role (ADR-0020: o nome viaja em rotas, na claim
/// curta <c>role</c> e na lista separada por ESPAÇO dos clients OIDC — espaço é
/// proibido por construção).
/// </summary>
internal static partial class RoleInputRules
{
    /// <summary>Tamanho máximo aceito para o nome de um role.</summary>
    public const int NameMaxLength = 100;

    [GeneratedRegex("^[a-zA-Z0-9](?:[a-zA-Z0-9._-]*[a-zA-Z0-9])?$")]
    private static partial Regex RoleName();

    /// <summary>Valida um nome de role já aparado.</summary>
    /// <param name="name">Nome candidato.</param>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name) && name.Length <= NameMaxLength && RoleName().IsMatch(name);

    /// <summary>
    /// Indica se o nome é reservado à estrutura de plataforma (ADR-0023/0024). O role
    /// <c>platform-operator</c> só pode nascer pelo seed de referência — criá-lo/gerí-lo via
    /// API num tenant de cliente forjaria um operador por colisão de nome (ADR-0020).
    /// Comparação case-insensitive: o Identity normaliza o nome, então quase-variações de
    /// caixa também não podem ser criadas.
    /// </summary>
    /// <param name="name">Nome candidato (já aparado).</param>
    public static bool IsReservedName(string name) =>
        string.Equals(name, SecureGatePlatform.OperatorRole, StringComparison.OrdinalIgnoreCase);
}
