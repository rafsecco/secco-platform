namespace Secco.SDK.AspNetCore.ReverseProxy;

/// <summary>
/// Opções de proxy reverso (ADR-0062). Desligado por padrão: sem <see cref="Enabled"/>, nenhum
/// cabeçalho <c>X-Forwarded-*</c> é honrado.
/// </summary>
public sealed class SeccoReverseProxyOptions
{
	/// <summary>Seção de configuração.</summary>
	public const string SectionKey = "Secco:ReverseProxy";

	/// <summary>Quando verdadeiro, honra <c>X-Forwarded-For</c> e <c>X-Forwarded-Proto</c> das redes conhecidas.</summary>
	public bool Enabled { get; set; }

	/// <summary>Redes (CIDR) de onde o proxy reverso fala com a aplicação. Obrigatória com <see cref="Enabled"/>.</summary>
	public List<string> KnownNetworks { get; set; } = [];
}
