namespace Secco.SharedKernel.Authorization;

/// <summary>Permissão declarada por um produto, com a descrição mostrada a quem administra perfis (ADR-0039).</summary>
/// <param name="Name">Nome canônico <c>recurso:acao</c> (<see cref="SeccoPermissions"/>).</param>
/// <param name="Description">Descrição curta em linguagem de negócio.</param>
public sealed record SeccoPermissionDefinition(string Name, string Description);
