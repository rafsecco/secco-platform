using Microsoft.Extensions.Hosting;
using Secco.SecureGate.Application.Clients;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>Tolera redirect URI <c>http://</c> apenas em Development — a mesma regra da configuração de plataforma.</summary>
internal sealed class HostRedirectUriPolicy(IHostEnvironment environment) : IRedirectUriPolicy
{
	public bool AllowInsecure { get; } = environment.IsDevelopment();
}
