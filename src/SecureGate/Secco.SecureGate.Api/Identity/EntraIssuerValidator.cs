using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Validação de issuer para o authority multi-tenant do Entra ID (ADR-0026): com o endpoint
/// <c>organizations</c> o issuer varia por diretório, então exige-se que ele seja exatamente
/// <c>https://login.microsoftonline.com/{tid}/v2.0</c> com o <c>tid</c> do próprio token.
/// Isto valida a COERÊNCIA do token; o gate de autorização real é o pin
/// <c>tid == directory id registrado do tenant</c>, feito no <see cref="EntraSignInProcessor"/>.
/// </summary>
internal static class EntraIssuerValidator
{
    /// <summary>Valida o issuer contra o template do Entra ID com o <c>tid</c> do token.</summary>
    /// <param name="issuer">Issuer declarado no token.</param>
    /// <param name="securityToken">Token em validação.</param>
    /// <param name="_">Parâmetros de validação em vigor (não usados — a regra é fixa).</param>
    /// <returns>O próprio issuer, quando válido.</returns>
    /// <exception cref="SecurityTokenInvalidIssuerException">Se o issuer não casa com o <c>tid</c> do token.</exception>
    internal static string Validate(string issuer, SecurityToken securityToken, TokenValidationParameters _)
    {
        if (securityToken is JsonWebToken token
            && token.TryGetPayloadValue<string>("tid", out var tid)
            && Guid.TryParse(tid, out var directoryId)
            && string.Equals(
                issuer,
                $"https://login.microsoftonline.com/{directoryId:D}/v2.0",
                StringComparison.Ordinal))
        {
            return issuer;
        }

        // Mensagem sem dados do token — o issuer inválido não é ecoado (ADR-0020)
        throw new SecurityTokenInvalidIssuerException(
            "O issuer do token não corresponde ao diretório (tid) declarado.");
    }
}
