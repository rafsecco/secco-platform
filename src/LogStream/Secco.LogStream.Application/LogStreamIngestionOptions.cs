namespace Secco.LogStream.Application;

/// <summary>
/// Limites de ingestão (ADR-0020 — negação de serviço): valores default do perfil
/// balanceado, configuráveis pelo adotante na seção <c>LogStream:Ingestion</c>.
/// São limites de runtime — o schema não os fixa.
/// </summary>
public sealed class LogStreamIngestionOptions
{
	/// <summary>Tamanho máximo da mensagem, em caracteres (default 16 KB).</summary>
	public int MaxMessageLength { get; set; } = 16_384;

	/// <summary>Tamanho máximo do stack trace, em caracteres (default 128 KB).</summary>
	public int MaxStackTraceLength { get; set; } = 131_072;

	/// <summary>Tamanho máximo do nome do serviço de origem de um log, em caracteres (default 256).</summary>
	public int MaxServiceNameLength { get; set; } = 256;

	/// <summary>Tamanho máximo da categoria do <c>ILogger</c> de origem, em caracteres (default 512).</summary>
	public int MaxCategoryLength { get; set; } = 512;

	/// <summary>Quantidade máxima de itens por batch (default 500).</summary>
	public int MaxBatchSize { get; set; } = 500;

	/// <summary>Tamanho máximo do nome de um processo (default 256).</summary>
	public int MaxProcessNameLength { get; set; } = 256;

	/// <summary>Tamanho máximo da referência externa de um processo (default 128).</summary>
	public int MaxExternalReferenceLength { get; set; } = 128;

	/// <summary>Capacidade da fila de ingestão em memória (default 10.000). Cheia → 503.</summary>
	public int QueueCapacity { get; set; } = 10_000;

	/// <summary>Tamanho máximo da URL de uma chamada de API (default 2048).</summary>
	public int MaxUrlLength { get; set; } = 2_048;

	/// <summary>Tamanho máximo de request/response body persistido (default 64 KB); acima disso, truncado.</summary>
	public int MaxBodyLength { get; set; } = 65_536;

	/// <summary>
	/// Headers adicionais (além da blocklist embutida: Authorization, Proxy-Authorization,
	/// Cookie, Set-Cookie, X-Api-Key, Api-Key) cujo valor é substituído por <c>[REDACTED]</c>
	/// antes de persistir (ADR-0020).
	/// </summary>
	public IList<string> RedactedHeaders { get; } = [];

	/// <summary>
	/// Tamanho máximo do identificador do ator de uma entrada de auditoria, em caracteres
	/// (default 256).
	/// </summary>
	public int MaxAuditActorIdLength { get; set; } = 256;

	/// <summary>Tamanho máximo do nome legível do ator de auditoria, em caracteres (default 256).</summary>
	public int MaxAuditActorNameLength { get; set; } = 256;

	/// <summary>Tamanho máximo da ação de uma entrada de auditoria, em caracteres (default 256).</summary>
	public int MaxAuditActionLength { get; set; } = 256;

	/// <summary>Tamanho máximo do tipo de recurso de uma entrada de auditoria, em caracteres (default 128).</summary>
	public int MaxAuditResourceTypeLength { get; set; } = 128;

	/// <summary>Tamanho máximo do identificador do recurso de uma entrada de auditoria, em caracteres (default 256).</summary>
	public int MaxAuditResourceIdLength { get; set; } = 256;

	/// <summary>
	/// Tamanho máximo do JSON de metadados de uma entrada de auditoria, em caracteres (default 16 KB).
	/// Ao contrário de corpo de requisição/resposta, este limite não trunca — a ingestão de auditoria
	/// é síncrona e rejeita com <c>400</c> (ver <c>CreateAuditEntryHandler</c>).
	/// </summary>
	public int MaxAuditMetadataLength { get; set; } = 16_384;
}
