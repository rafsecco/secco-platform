using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Secco.LogStream.Api.Endpoints;

/// <summary>
/// Limite de taxa das exportações, por pessoa (<c>sub</c>), além da concorrência da SDK (ADR-0058):
/// uma pessoa só não ocupa todas as vagas. O contador é da instância — com N réplicas o teto
/// efetivo é N vezes o configurado; perda do contador é tolerável (cache derivado, ADR-0035).
/// </summary>
public static class ExportRateLimiting
{
	/// <summary>Nome da política aplicada às rotas de exportação.</summary>
	public const string Policy = "exports";

	/// <summary>Seção com o limite (<c>PermitsPerMinute</c>).</summary>
	public const string SectionKey = "LogStream:Exports";

	private const int DefaultPermitsPerMinute = 10;

	/// <summary>Registra a política de limite por pessoa; <c>429</c> ao passar.</summary>
	/// <param name="services">Coleção de serviços.</param>
	public static IServiceCollection AddExportRateLimiting(this IServiceCollection services)
	{
		services.AddRateLimiter(_ => { });

		// Lido na montagem das options, não no registro: fontes de configuração tardias também valem.
		services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
		{
			var permits = configuration.GetValue($"{SectionKey}:PermitsPerMinute", DefaultPermitsPerMinute);

			if (permits < 1)
			{
				throw new InvalidOperationException($"'{SectionKey}:PermitsPerMinute' deve ser maior que zero.");
			}

			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
				context.User.FindFirst("sub")?.Value ?? string.Empty,
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = permits,
					Window = TimeSpan.FromMinutes(1),
					QueueLimit = 0,
				}));
		});

		return services;
	}
}
