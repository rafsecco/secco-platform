namespace Secco.SecureGate.Application.Federation;

/// <summary>Desfecho da etapa de atributos de pessoa do ciclo (ADR-0055).</summary>
public enum DirectoryAttributeSyncOutcome
{
	/// <summary>A etapa não rodou.</summary>
	NotRun,

	/// <summary>Aplicada (inclusive quando não havia nada a mudar).</summary>
	Completed,

	/// <summary>O diretório recusou a leitura dos atributos por falta de consentimento: campos inalterados.</summary>
	ConsentRequired,

	/// <summary>A leitura dos atributos falhou: nada mudou (fail-closed).</summary>
	ReadFailed,

	/// <summary>A limpeza em massa passou do freio: nada foi gravado.</summary>
	AbortedByBrake,
}

/// <summary>Resultado da etapa de atributos de pessoa do ciclo.</summary>
/// <param name="Outcome">Desfecho.</param>
/// <param name="Updated">Contas gravadas.</param>
/// <param name="Notices">Avisos do planejamento; nunca carregam conteúdo do diretório.</param>
/// <param name="ErrorCode">Código do erro, quando <see cref="Outcome"/> é <c>ReadFailed</c>.</param>
public sealed record DirectoryAttributeSyncResult(
	DirectoryAttributeSyncOutcome Outcome,
	int Updated = 0,
	IReadOnlyList<DirectoryAttributeNotice>? Notices = null,
	string? ErrorCode = null);
