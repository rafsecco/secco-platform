using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Variante da factory em que o SecureGate valida OS PRÓPRIOS tokens: a Authority aponta
/// para o próprio servidor (discovery/JWKS pelo backchannel in-memory) — exatamente a
/// configuração de produção, onde os endpoints de catálogo/gestão exigem tokens emitidos
/// pelo <c>/connect/token</c>. Substitui a chave HS256 de testes da factory base.
/// </summary>
public class SelfIssuedAuthSecureGateApiFactory : SecureGateApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secco:Authentication:Authority"] = "http://localhost",
                ["Secco:Authentication:RequireHttpsMetadata"] = "false",
                // Fontes posteriores vencem: apaga a chave HS256/issuer da factory base
                ["Secco:Authentication:DevelopmentSigningKey"] = "",
                ["Secco:Authentication:Issuer"] = "",
            }));

        builder.ConfigureServices(services =>
            // Configure (não PostConfigure) — mesma razão do teste cross-produto da 6.2.
            // O handler é lazy: o Server só pode ser tocado depois do host subir, e a
            // primeira validação de token acontece bem depois disso.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.BackchannelHttpHandler = new LazyBackchannelHandler(() => Server.CreateHandler())));
    }

    /// <summary>Cria o inner handler no primeiro uso — evita tocar <c>Server</c> durante a composição.</summary>
    private sealed class LazyBackchannelHandler(Func<HttpMessageHandler> handlerFactory) : DelegatingHandler
    {
        private readonly object _sync = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (InnerHandler is null)
            {
                lock (_sync)
                {
                    InnerHandler ??= handlerFactory();
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
