namespace Secco.SecureGate.Application.Provisioning;

/// <summary>Plano de provisionamento já validado, pronto para virar SQL.</summary>
/// <param name="Server">Servidor de destino.</param>
/// <param name="DatabaseName">Nome do database (validado por <see cref="DatabaseIdentifierPolicy"/>).</param>
/// <param name="LoginName">Nome do login/usuário de aplicação (idem).</param>
/// <param name="Password">Senha gerada para o usuário de aplicação. Nunca logar.</param>
/// <param name="CreateDatabase">
/// <c>true</c> cria o database; <c>false</c> assume que ele já existe e só cria login, usuário e
/// concessão — os dois cenários de adoção que a issue #3 descreve, com privilégios diferentes.
/// </param>
public sealed record TenantDatabaseProvisioningPlan(
	string Server,
	string DatabaseName,
	string LoginName,
	string Password,
	bool CreateDatabase);

/// <summary>Resultado de uma tentativa de execução automática.</summary>
/// <param name="Succeeded">Indica se a execução concluiu.</param>
/// <param name="FailureReason">
/// Classificação da falha, quando houve — nunca a exceção crua nem a connection string (ADR-0020).
/// </param>
public sealed record TenantDatabaseProvisioningExecution(bool Succeeded, string? FailureReason)
{
	/// <summary>Execução bem-sucedida.</summary>
	public static readonly TenantDatabaseProvisioningExecution Success = new(true, null);

	/// <summary>Execução falha, com a classificação informada.</summary>
	/// <param name="reason">Classificação da falha.</param>
	public static TenantDatabaseProvisioningExecution Failed(string reason) => new(false, reason);
}

/// <summary>
/// Provisionamento de banco de tenant em um engine específico.
/// </summary>
/// <remarks>
/// Um implementador por engine (ADR-0018: SQL Server é o default; PostgreSQL é o segundo e entra
/// na rodada seguinte com esta mesma abstração). A separação entre <see cref="BuildScript"/> e
/// <see cref="ExecuteAsync"/> é o que sustenta os dois modos do desenho: o script é sempre
/// produzido e é exatamente o que a execução automática aplica — nunca dois caminhos que possam
/// divergir.
/// </remarks>
public interface ITenantDatabaseProvisioner
{
	/// <summary>Nome do provider atendido, comparado ordinal ignore-case com o do alvo.</summary>
	string Provider { get; }

	/// <summary>Monta o SQL de provisionamento do plano.</summary>
	/// <param name="plan">Plano validado.</param>
	string BuildScript(TenantDatabaseProvisioningPlan plan);

	/// <summary>Monta a connection string de runtime do tenant resultante do plano.</summary>
	/// <param name="plan">Plano validado.</param>
	string BuildTenantConnectionString(TenantDatabaseProvisioningPlan plan);

	/// <summary>Aplica o script no servidor usando a credencial privilegiada do alvo.</summary>
	/// <param name="plan">Plano validado.</param>
	/// <param name="adminConnectionString">Credencial privilegiada. Nunca logar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantDatabaseProvisioningExecution> ExecuteAsync(
		TenantDatabaseProvisioningPlan plan,
		string adminConnectionString,
		CancellationToken cancellationToken = default);
}

/// <summary>Sonda de conectividade de um banco de tenant, para o painel de status.</summary>
/// <remarks>
/// Usa a <b>conexão de runtime</b> de cada tenant — a mesma que o produto usa — e por isso não
/// exige credencial privilegiada nenhuma. Foi a observação do adotante na issue #3, e ela se
/// sustenta: o catálogo já sabe tenant e banco; o que falta é saber se ele responde.
/// </remarks>
public interface ITenantDatabaseHealthProbe
{
	/// <summary>Nome do provider atendido.</summary>
	string Provider { get; }

	/// <summary>Abre a conexão e verifica se o banco responde.</summary>
	/// <param name="connectionString">Connection string de runtime do tenant. Nunca logar.</param>
	/// <param name="timeout">Tempo máximo da verificação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantDatabaseProbeResult> ProbeAsync(
		string connectionString,
		TimeSpan timeout,
		CancellationToken cancellationToken = default);
}

/// <summary>Resultado de uma sondagem de banco de tenant.</summary>
/// <param name="Reachable">Indica se o banco respondeu.</param>
/// <param name="FailureReason">
/// Classificação da falha, quando houve. Categoria, nunca a exceção crua — a mensagem de um erro
/// de conexão costuma conter servidor e usuário (ADR-0020).
/// </param>
public sealed record TenantDatabaseProbeResult(bool Reachable, string? FailureReason)
{
	/// <summary>Banco alcançável.</summary>
    public static readonly TenantDatabaseProbeResult Ok = new(true, null);

	/// <summary>Banco inalcançável, com a classificação informada.</summary>
	/// <param name="reason">Classificação da falha.</param>
	public static TenantDatabaseProbeResult Unreachable(string reason) => new(false, reason);
}
