# Secco.SharedKernel

Primitivas estáveis e puras da Secco Platform, compartilhadas por todos os produtos (ADR-0003).

- `Result` / `Result<T>` — resultado de operações de negócio sem exceções para controle de fluxo (ADR-0004).
- `Error` / `ErrorType` / `ValidationError` — erro de negócio com código estável e categoria semântica.
- `PageRequest` / `PagedResult<T>` — paginação 1-based com normalização silenciosa (default 20, teto 200) e metadados de navegação.
- `BaseEntity` — identidade Guid v7 (ordenável, amigável a índice clusterizado), igualdade por Id + tipo, eventos de domínio (`IDomainEvent`).
- `AuditableEntity` / `ISoftDeletable` — trilha de auditoria preenchida pelo interceptor do Secco.SDK; exclusão lógica opt-in.
- `SeccoException` / `DomainInvariantException` — exceções reservadas a infraestrutura e bugs (invariantes), nunca a fluxo de negócio.
- `SeccoClaims` / `SeccoHeaders` — nomes padronizados de claims (ADR-0007) e headers de correlação/tenancy.

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
