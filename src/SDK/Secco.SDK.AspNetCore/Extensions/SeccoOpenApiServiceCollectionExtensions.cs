using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição do OpenAPI da plataforma (ADR-0006) com as convenções da Secco.</summary>
public static class SeccoOpenApiServiceCollectionExtensions
{
    /// <summary>
    /// Registra o OpenAPI nativo do .NET com as convenções da plataforma. Drop-in do
    /// <c>AddOpenApi()</c>. Convenção atual: enums serializados como string
    /// (<c>JsonStringEnumConverter</c>) são declarados no schema com <c>type: string</c> —
    /// sem isso o .NET 10 emite os valores string mas omite o tipo, e o gerador de client
    /// (NSwag) assume enum numérico, quebrando a desserialização (ADR-0006: contrato correto
    /// de raiz, sem correções manuais no client gerado).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSeccoOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(options =>
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type.IsEnum)
                {
                    schema.Type = JsonSchemaType.String;
                }

                return Task.CompletedTask;
            }));

        return services;
    }
}
