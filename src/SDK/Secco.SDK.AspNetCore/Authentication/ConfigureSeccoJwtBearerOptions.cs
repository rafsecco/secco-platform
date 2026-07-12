using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Secco.SharedKernel.Constants;

namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>
/// Aplica as decisões da ADR-0007 ao JwtBearer: claims curtas sem remapeamento automático
/// (<c>MapInboundClaims = false</c>), <c>NameClaimType = "sub"</c>, <c>RoleClaimType = "role"</c>,
/// e o modo de validação (Authority/JWKS ou chave simétrica de desenvolvimento).
/// </summary>
internal sealed class ConfigureSeccoJwtBearerOptions(
    IOptions<SeccoAuthenticationOptions> authenticationOptions,
    IHostEnvironment environment) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var secco = authenticationOptions.Value;

        // ADR-0007: nunca remapear claims curtas para as URIs longas de ClaimTypes
        options.MapInboundClaims = false;

        options.TokenValidationParameters.NameClaimType = SeccoClaims.Subject;
        options.TokenValidationParameters.RoleClaimType = SeccoClaims.Role;
        options.TokenValidationParameters.ValidAudience = secco.Audience;

        if (!string.IsNullOrWhiteSpace(secco.Authority))
        {
            options.Authority = secco.Authority;
            // Em Production o HTTPS do discovery não é negociável
            options.RequireHttpsMetadata = secco.RequireHttpsMetadata || environment.IsProduction();

            if (!string.IsNullOrWhiteSpace(secco.Issuer))
            {
                options.TokenValidationParameters.ValidIssuer = secco.Issuer;
            }

            return;
        }

        // Modo de desenvolvimento (HS256) — o validador já garantiu ambiente e tamanho da chave
        options.TokenValidationParameters.ValidIssuer = secco.Issuer;
        options.TokenValidationParameters.IssuerSigningKey =
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secco.DevelopmentSigningKey!));
    }

    public void Configure(JwtBearerOptions options) =>
        Configure(JwtBearerDefaults.AuthenticationScheme, options);
}
