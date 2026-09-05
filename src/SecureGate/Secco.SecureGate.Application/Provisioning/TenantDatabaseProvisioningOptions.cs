namespace Secco.SecureGate.Application.Provisioning;

/// <summary>
/// Alvo de provisionamento: um servidor de banco onde bancos de tenant podem ser criados
/// (seção <c>SecureGate:Provisioning:Targets:&lt;nome&gt;</c>).
/// </summary>
public sealed class ProvisioningTarget
{
	/// <summary>Provider do alvo (hoje apenas <c>SqlServer</c>).</summary>
	public string? Provider { get; set; }

	/// <summary>Servidor, no formato aceito pelo provider (ex.: <c>sql-01.interno,1433</c>).</summary>
	public string? Server { get; set; }

	/// <summary>
	/// Credencial privilegiada, capaz de criar database e login. <b>Ausente = automação
	/// desligada para este alvo</b>: o provisionamento segue funcionando em modo script.
	/// Nunca é logada, nunca entra em mensagem de erro, nunca volta em resposta.
	/// </summary>
	public string? AdminConnectionString { get; set; }

	/// <summary>Indica se este alvo pode executar o provisionamento por conta própria.</summary>
	public bool CanExecute => !string.IsNullOrWhiteSpace(AdminConnectionString);
}

/// <summary>
/// Provisionamento de bancos de tenant (seção <c>SecureGate:Provisioning</c>).
/// </summary>
/// <remarks>
/// A ausência completa da seção não desliga o recurso — desliga apenas a <b>automação</b>. O modo
/// script continua disponível, porque ele não precisa de privilégio nenhum: o SecureGate gera o
/// SQL e um DBA aplica. Essa é a decisão central do desenho: muitos DBAs corporativos jamais
/// concedem <c>dbcreator</c> a uma aplicação, e um recurso que só funciona com esse privilégio
/// não serviria a esses adotantes.
/// </remarks>
public sealed class TenantDatabaseProvisioningOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "SecureGate:Provisioning";

	/// <summary>Nome do alvo assumido quando a requisição não informa outro.</summary>
	public const string DefaultTargetName = "default";

	/// <summary>Alvos declarados, por nome.</summary>
	public Dictionary<string, ProvisioningTarget> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Resolve um alvo pelo nome; nulo se não declarado.</summary>
	/// <param name="name">Nome do alvo; vazio usa <see cref="DefaultTargetName"/>.</param>
	public ProvisioningTarget? Resolve(string? name) =>
		Targets.TryGetValue(
			string.IsNullOrWhiteSpace(name) ? DefaultTargetName : name.Trim(),
			out var target)
			? target
			: null;

	/// <summary>
	/// Valida os alvos declarados. Alvo declarado pela metade é erro de startup, não degradação
	/// silenciosa (ADR-0020) — mesma postura de <c>SecureGateClientCredentialsOptions</c>. Devolve
	/// o resultado em vez de lançar: erro de configuração é fluxo previsto, não excepcional (ADR-0004).
	/// </summary>
	/// <param name="error">Descrição do primeiro problema encontrado; nula quando válido.</param>
	public bool TryValidate(out string? error)
	{
		foreach (var (name, target) in Targets)
		{
			if (string.IsNullOrWhiteSpace(target.Server))
			{
				error = $"'{SectionKey}:Targets:{name}:Server' é obrigatório quando o alvo é declarado.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(target.Provider))
			{
				error = $"'{SectionKey}:Targets:{name}:Provider' é obrigatório quando o alvo é declarado.";
				return false;
			}
		}

		error = null;
		return true;
	}
}
