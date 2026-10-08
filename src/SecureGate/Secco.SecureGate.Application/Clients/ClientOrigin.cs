namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Por onde um client OIDC nasceu (ADR-0037). Cada caminho só enxerga e altera os seus: a
/// reconciliação da configuração nunca toca client da API, e a API nunca toca client de plataforma.
/// </summary>
public enum ClientOrigin
{
	/// <summary>Client de PLATAFORMA, sem tenant, declarado em <c>SecureGate:PlatformClients</c>.</summary>
	Configuration = 0,

	/// <summary>Client de PRODUTO, vinculado a um tenant, registrado pela API.</summary>
	Api = 1,
}
