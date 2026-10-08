namespace Secco.SecureGate.Tests.Integration;

/// <summary>SecureGate com dois clients de plataforma declarados (ADR-0037).</summary>
public sealed class PlatformClientsSecureGateApiFactory : SecureGateApiFactory
{
	public const string MachineId = "secco-teste-maquina";
	public const string MachineSecret = "maquina-de-plataforma-secret-32-chars!";
	public const string PortalId = "secco-teste-portal";
	public const string PortalSecret = "portal-de-plataforma-secret-32-chars!!";

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		base.ConfigureTestConfiguration(settings);
		ArgumentNullException.ThrowIfNull(settings);

		settings["SecureGate:PlatformClients:0:ClientId"] = MachineId;
		settings["SecureGate:PlatformClients:0:Type"] = "ClientCredentials";
		settings["SecureGate:PlatformClients:0:ClientSecret"] = MachineSecret;
		settings["SecureGate:PlatformClients:0:Scopes:0"] = "catalog:logstream";
		settings["SecureGate:PlatformClients:0:Roles:0"] = "leitor";

		settings["SecureGate:PlatformClients:1:ClientId"] = PortalId;
		settings["SecureGate:PlatformClients:1:Type"] = "AuthorizationCode";
		settings["SecureGate:PlatformClients:1:ClientSecret"] = PortalSecret;
		settings["SecureGate:PlatformClients:1:Scopes:0"] = "logstream";
		settings["SecureGate:PlatformClients:1:RedirectUris:0"] = "https://portal.testes.local/signin-oidc";
		settings["SecureGate:PlatformClients:1:PostLogoutRedirectUris:0"] = "https://portal.testes.local/signout-callback-oidc";
	}
}
