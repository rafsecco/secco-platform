using Secco.SDK.Email;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Infrastructure.Credentials;

namespace Secco.SecureGate.Api.Extensions;

/// <summary>
/// Composição do ciclo de credencial (ADR-0033): envio de e-mail e validade dos dois provedores
/// de token do Identity. As <see cref="CredentialOptions"/> em si são bindadas e validadas pela
/// Infrastructure, junto das demais seções <c>SecureGate:*</c>.
/// </summary>
public static class SecureGateCredentialsExtensions
{
	/// <summary>Registra a porta de e-mail e as validades dos tokens de convite e redefinição.</summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddSecureGateCredentials(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Sem e-mail não há convite nem recuperação, e o admin não pode mais definir senha
		// (ADR-0033) — por isso a seção é validada no startup, e não no primeiro envio.
		services.AddSeccoEmail("SecureGate:Email");

		// Validades tiradas das options; nada estático. Um tipo de options por provedor é o que
		// permite janelas diferentes para convite (longa) e redefinição (curta).
		services.AddOptions<InviteTokenProviderOptions>().Configure<CredentialOptions>((options, credentials) =>
			options.TokenLifespan = TimeSpan.FromHours(credentials.InviteLifetimeHours));
		services.AddOptions<ResetTokenProviderOptions>().Configure<CredentialOptions>((options, credentials) =>
			options.TokenLifespan = TimeSpan.FromMinutes(credentials.ResetLifetimeMinutes));

		return services;
	}
}
