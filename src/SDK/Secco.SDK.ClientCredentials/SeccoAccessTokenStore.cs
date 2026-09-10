namespace Secco.SDK.ClientCredentials;

/// <summary>
/// Estado compartilhado de um token de acesso (singleton POR PIPELINE): o pipeline de
/// handlers do <c>IHttpClientFactory</c> é reciclado periodicamente — o cache do token
/// não pode morrer com ele. Cada recurso que consome client credentials deve manter o
/// PRÓPRIO store, pois pede um token com o próprio scope (least privilege, ADR-0020).
/// </summary>
public sealed class SeccoAccessTokenStore
{
	/// <summary>Serializa a renovação do token entre requisições concorrentes.</summary>
	internal SemaphoreSlim RefreshLock { get; } = new(1, 1);

	/// <summary>Token vigente; nulo antes da primeira aquisição.</summary>
	internal string? AccessToken { get; set; }

	/// <summary>Expiração do token vigente.</summary>
	internal DateTimeOffset ExpiresAt { get; set; }
}
