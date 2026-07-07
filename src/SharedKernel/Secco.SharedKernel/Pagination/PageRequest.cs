namespace Secco.SharedKernel.Pagination;

/// <summary>
/// Requisição de página para listagens (ADR-0003). Carrega apenas paginação:
/// ordenação e busca são política de cada produto e declaradas por endpoint.
/// Valores fora dos limites são normalizados silenciosamente — nunca lançam.
/// </summary>
public sealed record PageRequest
{
    /// <summary>Número da primeira página (paginação 1-based).</summary>
    public const int FirstPage = 1;

    /// <summary>Tamanho de página aplicado quando nenhum (ou um inválido) é informado.</summary>
    public const int DefaultSize = 20;

    /// <summary>Tamanho máximo de página; valores maiores são reduzidos a este teto.</summary>
    public const int MaxSize = 200;

    /// <summary>Requisição padrão: primeira página com <see cref="DefaultSize"/> itens.</summary>
    public static readonly PageRequest Default = new();

    /// <summary>
    /// Cria uma requisição de página normalizando os valores: página menor que
    /// <see cref="FirstPage"/> vira <see cref="FirstPage"/>; tamanho não positivo vira
    /// <see cref="DefaultSize"/>; tamanho acima de <see cref="MaxSize"/> é reduzido ao teto.
    /// </summary>
    /// <param name="page">Número da página desejada (1-based).</param>
    /// <param name="size">Quantidade de itens por página.</param>
    public PageRequest(int page = FirstPage, int size = DefaultSize)
    {
        Page = page < FirstPage ? FirstPage : page;
        Size = size switch
        {
            < 1 => DefaultSize,
            > MaxSize => MaxSize,
            _ => size,
        };
    }

    /// <summary>Número da página (1-based), já normalizado.</summary>
    public int Page { get; }

    /// <summary>Itens por página, já normalizado para o intervalo [1, <see cref="MaxSize"/>].</summary>
    public int Size { get; }

    /// <summary>Quantidade de itens a pular (offset) para alcançar esta página.</summary>
    public int Skip => (Page - FirstPage) * Size;
}
