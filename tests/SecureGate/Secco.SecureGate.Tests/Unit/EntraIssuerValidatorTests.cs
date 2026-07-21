using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SecureGate.Api.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Validação do issuer multi-tenant do Entra ID (ADR-0026): o issuer precisa casar
/// exatamente o template <c>https://login.microsoftonline.com/{tid}/v2.0</c> com o
/// <c>tid</c> do próprio token — coerência interna; o pin de diretório é do processor.
/// </summary>
public class EntraIssuerValidatorTests
{
    private static readonly TokenValidationParameters Parameters = new();

    private static JsonWebToken CreateToken(string issuer, object? tid)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Claims = tid is null ? [] : new Dictionary<string, object> { ["tid"] = tid },
        };

        // Sem credenciais de assinatura: token "alg none" — suficiente para o validador,
        // que só inspeciona o payload (a assinatura é validada pelo handler OIDC)
        return new JsonWebToken(new JsonWebTokenHandler().CreateToken(descriptor));
    }

    [Fact]
    public void Validate_WithIssuerMatchingTid_ReturnsIssuer()
    {
        var tid = Guid.NewGuid();
        var issuer = $"https://login.microsoftonline.com/{tid:D}/v2.0";
        var token = CreateToken(issuer, tid.ToString("D"));

        var result = EntraIssuerValidator.Validate(issuer, token, Parameters);

        result.Should().Be(issuer);
    }

    [Fact]
    public void Validate_WithIssuerOfAnotherDirectory_Throws()
    {
        var issuer = $"https://login.microsoftonline.com/{Guid.NewGuid():D}/v2.0";
        var token = CreateToken(issuer, Guid.NewGuid().ToString("D"));

        var act = () => EntraIssuerValidator.Validate(issuer, token, Parameters);

        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void Validate_WithoutTidClaim_Throws()
    {
        var issuer = $"https://login.microsoftonline.com/{Guid.NewGuid():D}/v2.0";
        var token = CreateToken(issuer, tid: null);

        var act = () => EntraIssuerValidator.Validate(issuer, token, Parameters);

        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void Validate_WithNonGuidTid_Throws()
    {
        var issuer = "https://login.microsoftonline.com/nao-e-guid/v2.0";
        var token = CreateToken(issuer, "nao-e-guid");

        var act = () => EntraIssuerValidator.Validate(issuer, token, Parameters);

        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void Validate_WithWrongTemplate_Throws()
    {
        var tid = Guid.NewGuid();
        // Host certo, esquema errado (http) — template exige igualdade exata
        var issuer = $"http://login.microsoftonline.com/{tid:D}/v2.0";
        var token = CreateToken(issuer, tid.ToString("D"));

        var act = () => EntraIssuerValidator.Validate(issuer, token, Parameters);

        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }
}
