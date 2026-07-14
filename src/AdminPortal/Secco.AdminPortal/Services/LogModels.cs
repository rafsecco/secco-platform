namespace Secco.AdminPortal.Services;

/// <summary>Filtros da busca de logs de um tenant.</summary>
/// <param name="From">Início do período (opcional).</param>
/// <param name="To">Fim do período (opcional).</param>
/// <param name="Level">Nível mínimo/exato (nome do enum; nulo = todos).</param>
/// <param name="Message">Trecho da mensagem (LIKE; opcional).</param>
/// <param name="Page">Página (1-based).</param>
/// <param name="Size">Tamanho da página.</param>
public sealed record LogEntryFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Level = null,
    string? Message = null,
    int Page = 1,
    int Size = 25);

/// <summary>Registro de log projetado para a tela.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Level">Nível (nome).</param>
/// <param name="Message">Mensagem.</param>
/// <param name="CreatedAt">Momento do registro.</param>
/// <param name="CorrelationId">Correlation id, se houver.</param>
public sealed record LogEntryView(Guid Id, string Level, string Message, DateTimeOffset CreatedAt, Guid? CorrelationId);

/// <summary>Página de resultados da busca de logs.</summary>
/// <param name="Items">Registros da página.</param>
/// <param name="Page">Página atual (1-based).</param>
/// <param name="Size">Tamanho da página.</param>
/// <param name="TotalCount">Total de registros.</param>
/// <param name="TotalPages">Total de páginas.</param>
public sealed record LogEntryPage(
    IReadOnlyList<LogEntryView> Items, int Page, int Size, long TotalCount, int TotalPages);
