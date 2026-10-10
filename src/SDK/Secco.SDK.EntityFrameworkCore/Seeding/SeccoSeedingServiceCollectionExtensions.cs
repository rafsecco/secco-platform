using Microsoft.Extensions.DependencyInjection;

namespace Secco.SDK.EntityFrameworkCore.Seeding;

/// <summary>Registro de seeders de referência por delegate (ADR-0039).</summary>
public static class SeccoSeedingServiceCollectionExtensions
{
	/// <summary>
	/// Registra um <see cref="IReferenceDataSeeder"/> que executa o delegate no escopo do seed. Serve
	/// a pacotes que não referenciam este SDK (ex.: o client do SecureGate) — o produto liga os dois.
	/// </summary>
	public static IServiceCollection AddSeccoReferenceSeeder(
		this IServiceCollection services, Func<IServiceProvider, CancellationToken, Task> seed, int order = 0)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(seed);

		services.AddScoped<IReferenceDataSeeder>(serviceProvider => new DelegateReferenceDataSeeder(serviceProvider, seed, order));
		return services;
	}

	private sealed class DelegateReferenceDataSeeder(
		IServiceProvider serviceProvider, Func<IServiceProvider, CancellationToken, Task> seed, int order) : IReferenceDataSeeder
	{
		public int Order => order;

		public Task SeedAsync(CancellationToken cancellationToken = default) => seed(serviceProvider, cancellationToken);
	}
}
