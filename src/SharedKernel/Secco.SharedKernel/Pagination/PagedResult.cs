namespace Secco.SharedKernel.Pagination;

/// <summary>
/// Fábricas de <see cref="PagedResult{T}"/> (espelha o padrão <c>Result</c>/<c>Result&lt;T&gt;</c>).
/// </summary>
public static class PagedResult
{
    /// <summary>Cria uma página de resultados a partir da requisição que a originou.</summary>
    /// <typeparam name="T">Tipo dos itens da página.</typeparam>
    /// <param name="items">Itens da página atual.</param>
    /// <param name="request">Requisição de página que originou a consulta.</param>
    /// <param name="totalCount">Total de itens existentes na consulta, em todas as páginas.</param>
    public static PagedResult<T> Create<T>(IReadOnlyList<T> items, PageRequest request, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PagedResult<T>(items, request.Page, request.Size, totalCount);
    }

    /// <summary>Cria uma página vazia (zero itens, zero total) para a requisição informada.</summary>
    /// <typeparam name="T">Tipo dos itens da página.</typeparam>
    /// <param name="request">Requisição de página que originou a consulta.</param>
    public static PagedResult<T> Empty<T>(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PagedResult<T>([], request.Page, request.Size, 0);
    }
}

/// <summary>
/// Página de resultados de uma listagem (ADR-0003): itens da página atual
/// mais os metadados de paginação necessários para navegação.
/// </summary>
/// <typeparam name="T">Tipo dos itens da página.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>
    /// Cria uma página de resultados.
    /// </summary>
    /// <param name="items">Itens da página atual.</param>
    /// <param name="page">Número da página (1-based).</param>
    /// <param name="size">Itens por página solicitados.</param>
    /// <param name="totalCount">Total de itens existentes na consulta, em todas as páginas.</param>
    /// <exception cref="ArgumentOutOfRangeException">Se página, tamanho ou total forem inválidos.</exception>
    public PagedResult(IReadOnlyList<T> items, int page, int size, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, PageRequest.FirstPage);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

        Items = items;
        Page = page;
        Size = size;
        TotalCount = totalCount;
    }

    /// <summary>Itens da página atual.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Número da página atual (1-based).</summary>
    public int Page { get; }

    /// <summary>Itens por página solicitados (a última página pode conter menos).</summary>
    public int Size { get; }

    /// <summary>Total de itens existentes na consulta, em todas as páginas.</summary>
    public long TotalCount { get; }

    /// <summary>Total de páginas; zero quando a consulta não tem itens.</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)Size);

    /// <summary>Indica se existe página anterior a esta.</summary>
    public bool HasPreviousPage => Page > PageRequest.FirstPage;

    /// <summary>Indica se existe página posterior a esta.</summary>
    public bool HasNextPage => Page < TotalPages;
}
