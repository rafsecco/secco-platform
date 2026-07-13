using System.Security.Cryptography.X509Certificates;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.OpenIddict;

namespace Secco.SecureGate.Api.Extensions;

/// <summary>
/// Composição do servidor OIDC (ADR-0022): client credentials + JWKS/discovery.
/// Access tokens são JWT assinados <b>sem</b> criptografia de conteúdo — os produtos
/// os validam com o <c>JwtBearer</c> padrão do SDK, sem conhecer o OpenIddict.
/// </summary>
public static class SecureGateOpenIddictExtensions
{
    /// <summary>
    /// Registra o OpenIddict (core sobre o <see cref="SecureGateDbContext"/> + servidor).
    /// Certificados: fora de Production, automáticos (efêmeros em Testing, certificado de
    /// desenvolvimento persistido nos demais); em Production, <c>SecureGate:Signing:CertificatePath</c>
    /// é obrigatório — sem ele a API não sobe (fail-fast, ADR-0020).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="environment">Ambiente de hospedagem.</param>
    /// <param name="configuration">Configuração do host.</param>
    public static IServiceCollection AddSecureGateOpenIddict(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var accessTokenLifetimeMinutes = configuration.GetValue("SecureGate:Tokens:AccessTokenLifetimeMinutes", 60);

        services.AddOpenIddict()
            .AddCore(core => core
                .UseEntityFrameworkCore()
                .UseDbContext<SecureGateDbContext>()
                .ReplaceDefaultEntities<OidcApplication, OidcAuthorization, OidcScope, OidcToken, Guid>())
            .AddServer(server =>
            {
                server.SetTokenEndpointUris("connect/token");
                server.AllowClientCredentialsFlow();

                // JWT puro: validável por qualquer JwtBearer via JWKS (ADR-0022 — produtos
                // agnósticos do OpenIddict). O conteúdo do access token não carrega segredos.
                server.DisableAccessTokenEncryption();

                server.SetAccessTokenLifetime(TimeSpan.FromMinutes(accessTokenLifetimeMinutes));

                ConfigureCertificates(server, environment, configuration);

                var aspNetCore = server.UseAspNetCore();
                aspNetCore.EnableTokenEndpointPassthrough();

                if (!environment.IsProduction())
                {
                    // TestServer/dev local são HTTP; em Production o HTTPS segue obrigatório
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            });

        return services;
    }

    private static void ConfigureCertificates(
        OpenIddictServerBuilder server,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (environment.IsProduction())
        {
            var certificatePath = configuration["SecureGate:Signing:CertificatePath"];
            var certificatePassword = configuration["SecureGate:Signing:CertificatePassword"];

            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                throw new InvalidOperationException(
                    "'SecureGate:Signing:CertificatePath' é obrigatório em Production — o SecureGate não sobe sem certificado explícito (fail-fast, ADR-0020).");
            }

            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword);
            server.AddSigningCertificate(certificate);
            server.AddEncryptionCertificate(certificate);
            return;
        }

        if (environment.IsEnvironment("Testing"))
        {
            // Chaves em memória: nada persiste entre execuções de teste
            server.AddEphemeralSigningKey();
            server.AddEphemeralEncryptionKey();
            return;
        }

        // DEV/Staging: certificados de desenvolvimento gerados e persistidos pelo OpenIddict
        server.AddDevelopmentSigningCertificate();
        server.AddDevelopmentEncryptionCertificate();
    }
}
