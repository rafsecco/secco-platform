using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Tests.Integration.Helpers;

/// <summary>
/// Gera tokens HS256 compatíveis com a configuração de testes da factory base —
/// para exercitar a autorização por scope dos endpoints sem o fluxo OIDC completo
/// (que os testes com <see cref="SelfIssuedAuthSecureGateApiFactory"/> cobrem).
/// </summary>
internal static class JwtTestTokenFactory
{
    public const string SigningKey = "chave-de-testes-com-32-caracteres!!";
    public const string Issuer = "secco-tests";
    public const string Audience = "secco-securegate";

    /// <summary>Cria um token com os scopes informados (claim curta <c>scope</c>, ADR-0007).</summary>
    /// <param name="scopes">Scopes concedidos (separados por espaço na claim, RFC 8693).</param>
    public static string CreateToken(params string[] scopes) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                [SeccoClaims.Subject] = "test-admin",
                ["scope"] = string.Join(' ', scopes),
            },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
}
