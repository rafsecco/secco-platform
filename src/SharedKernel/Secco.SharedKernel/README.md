# Secco.SharedKernel

Primitivas estáveis e puras da Secco Platform, compartilhadas por todos os produtos (ADR-0003).

- `Result` / `Result<T>` — resultado de operações de negócio sem exceções para controle de fluxo (ADR-0004).
- `Error` / `ErrorType` / `ValidationError` — erro de negócio com código estável e categoria semântica.
- `PageRequest` / `PagedResult<T>` — paginação 1-based com normalização silenciosa (default 20, teto 200) e metadados de navegação.

## Regras de admissão (ADR-0003)

Todo tipo deste pacote atende, obrigatoriamente: uso por dois ou mais produtos, zero dependências além da BCL, sem I/O e sem estado, interface estável.

## Uso

```csharp
public async Task<Result<LogEntryDto>> Handle(CreateLogEntryCommand cmd, CancellationToken ct)
{
    if (tenant is null)
        return PlatformErrors.Tenant.NotResolved; // Error → Result<T> implícito

    return dto; // T → Result<T> implícito
}
```
