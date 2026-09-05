namespace Secco.NotificationHub.Application;

/// <summary>
/// Limites de entrada do produto (ADR-0020), configuráveis na seção
/// <c>NotificationHub:Limits</c> — bind lazy feito pela Infrastructure.
/// </summary>
public sealed class NotificationHubOptions
{
	/// <summary>Tamanho máximo do destinatário de e-mail (default 254 — teto prático de um e-mail, RFC 5321).</summary>
	public int MaxRecipientLength { get; set; } = 254;

	/// <summary>Tamanho máximo do título (default 998 — mesmo teto prático de uma linha de header, RFC 5322, quando vira assunto de e-mail).</summary>
	public int MaxTitleLength { get; set; } = 998;

	/// <summary>Tamanho máximo da mensagem (default 64 KB).</summary>
	public int MaxMessageLength { get; set; } = 65_536;

	/// <summary>Tamanho máximo da origem (texto livre, Fase 8.4 — default 128).</summary>
	public int MaxSourceLength { get; set; } = 128;

	/// <summary>Tamanho máximo do tipo (texto livre, Fase 8.4 — default 128).</summary>
	public int MaxTypeLength { get; set; } = 128;

	/// <summary>Tamanho máximo do link (Fase 8.4 — default 2048, teto prático de URL).</summary>
	public int MaxLinkLength { get; set; } = 2_048;

	/// <summary>
	/// Máximo de destinos por lote (issue #15 — default 500).
	/// </summary>
	/// <remarks>
	/// O teto existe porque o lote enfileira um job por destino de e-mail: 
	/// as gravações das notificações vão numa ida só ao banco, mas o enfileiramento continua
	/// sendo N inserções na fila do Hangfire, dentro da requisição. Um lote sem limite viraria
	/// requisição longa e pressão na fila (ADR-0020).
	/// </remarks>
	public int MaxBatchDestinations { get; set; } = 500;
}
