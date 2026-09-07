namespace Secco.SDK.AspNetCore.BackgroundJobs;

/// <summary>
/// Agendamento de jobs persistentes com retry automático em falha transitória
/// (ADR-0015 Camada 2). Produtos nunca acoplam ao Hangfire diretamente — só a esta
/// abstração, que permite trocar a implementação sem tocar código de produto.
/// </summary>
public interface IBackgroundJobScheduler
{
	/// <summary>
	/// Enfileira <typeparamref name="TJob"/> para execução em background. O
	/// <paramref name="tenantId"/> é restaurado automaticamente no escopo de execução
	/// antes do job rodar (ADR-0005/0015): o job em si nunca lida com tenant restoration.
	/// </summary>
	/// <typeparam name="TJob">Tipo do job, resolvido via DI no momento da execução.</typeparam>
	/// <typeparam name="TPayload">Tipo do payload, serializado pelo storage do Hangfire.</typeparam>
	/// <param name="tenantId">Tenant do item de trabalho.</param>
	/// <param name="payload">Dados de entrada do job.</param>
	/// <returns>Identificador do job no storage (correlação em logs/diagnóstico).</returns>
	string Enqueue<TJob, TPayload>(Guid tenantId, TPayload payload)
		where TJob : IBackgroundJob<TPayload>;

	/// <summary>
	/// Agenda <typeparamref name="TJob"/> para execução em background no instante informado. O
	/// <paramref name="tenantId"/> é restaurado automaticamente no escopo de execução antes do
	/// job rodar — igual ao <see cref="Enqueue{TJob, TPayload}"/> — e um
	/// <paramref name="enqueueAt"/> no passado faz o Hangfire enfileirar imediatamente, em vez
	/// de recusar o agendamento.
	/// </summary>
	/// <typeparam name="TJob">Tipo do job, resolvido via DI no momento da execução.</typeparam>
	/// <typeparam name="TPayload">Tipo do payload, serializado pelo storage do Hangfire.</typeparam>
	/// <param name="tenantId">Tenant do item de trabalho.</param>
	/// <param name="payload">Dados de entrada do job.</param>
	/// <param name="enqueueAt">Instante em que o job deve ser enfileirado para execução.</param>
	/// <returns>Identificador do job no storage (correlação em logs/diagnóstico).</returns>
	string Schedule<TJob, TPayload>(Guid tenantId, TPayload payload, DateTimeOffset enqueueAt)
		where TJob : IBackgroundJob<TPayload>;
}
