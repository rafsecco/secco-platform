using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SharedKernel.Constants;

namespace Secco.LogStream.Tests.Integration.Helpers;

/// <summary>Gera tokens HS256 compatíveis com a configuração de testes da factory.</summary>
internal static class JwtTestTokenFactory
{
    public const string SigningKey = "chave-de-testes-com-32-caracteres!!";
    public const string Issuer = "secco-tests";
    public const string Audience = "secco-logstream";

    public static string CreateToken(Guid tenantId, string subject = "test-user") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = new Dictionary<string, object>
            {
                [SeccoClaims.Subject] = subject,
                [SeccoClaims.TenantId] = tenantId.ToString(),
            },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
}
