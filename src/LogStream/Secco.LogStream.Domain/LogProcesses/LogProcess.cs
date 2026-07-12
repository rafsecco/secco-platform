using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.LogStream.Domain.LogProcesses;

/// <summary>
/// Processo de negócio monitorado (ex.: <c>ImportacaoPedidos</c>): agrega
/// <see cref="LogProcessDetail"/>s e tem status derivado do pior nível entre eles
/// (<see cref="ProcessStatusRule"/>). O Id Guid v7 existe antes da persistência —
/// o chamador recebe <c>202</c> com o Id e já pode enviar details.
/// </summary>
public sealed class LogProcess : BaseEntity
{
    private readonly List<LogProcessDetail> _details = [];

    private LogProcess()
    {
        // Construtor de rehidratação do EF Core
        Name = string.Empty;
    }

    /// <summary>Cria um processo.</summary>
    /// <param name="name">Nome do processo. Obrigatório.</param>
    /// <param name="externalReference">Identificador de negócio do chamador (número do job, id do lote...), sem semântica imposta.</param>
    /// <param name="correlationId">Correlation id da requisição de origem, quando propagado.</param>
    /// <exception cref="DomainInvariantException">Se o nome for nulo ou vazio.</exception>
    public LogProcess(string name, string? externalReference = null, Guid? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainInvariantException("Um processo exige nome não vazio.");
        }

        Name = name;
        ExternalReference = externalReference;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Nome do processo (coluna <c>ds_name</c>).</summary>
    public string Name { get; private set; }

    /// <summary>Referência externa do chamador (coluna <c>ds_external_reference</c>).</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>Correlation id da requisição de origem (coluna <c>correlation_id</c>).</summary>
    public Guid? CorrelationId { get; private set; }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Details do processo (tabela <c>tb_log_process_details</c>, cascade delete).</summary>
    public IReadOnlyCollection<LogProcessDetail> Details => _details;
}
