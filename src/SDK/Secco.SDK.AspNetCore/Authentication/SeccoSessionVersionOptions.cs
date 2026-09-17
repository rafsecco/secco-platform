namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>Opções da verificação de versão de sessão (seção <c>Secco:Authentication</c>, ADR-0032).</summary>
public sealed class SeccoSessionVersionOptions
{
	/// <summary>TTL do cache por usuário, em segundos. Janela máxima entre revogar e recusar.</summary>
	public int SessionVersionCacheTtlSeconds { get; set; } = 60;
}
