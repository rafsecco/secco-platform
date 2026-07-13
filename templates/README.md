# Secco.Templates

O padrão executável da Secco Platform (ADR-0013): novo produto **nasce do template**, nunca de cópia manual — e divergência entre o template e as ADRs é bug de prioridade alta (o CI instancia, builda e testa o produto gerado a cada mudança).

## Uso (no monorepo)

```bash
dotnet new install ./templates/secco-service
dotnet new secco-service -n Secco.NotificationHub -o src/NotificationHub
```

O produto gerado vem com: 4 camadas (ADR-0002), `AddSeccoPlatform()` composto (ADR-0004), multi-tenancy database-per-tenant (ADR-0005), contrato OpenAPI versionado + client NSwag gerado no build (ADR-0006), nomenclatura de banco por convention (ADR-0017), migrations por engine (ADR-0018), testes unitários + integração com Testcontainers (ADR-0012), Dockerfile e compose — mais um recurso **Sample** completo como referência executável dos padrões (apagável).

Siga o checklist pós-geração no `README.md` do produto gerado (mover testes, gerar migrations, snapshot do contrato, solution, CI).
