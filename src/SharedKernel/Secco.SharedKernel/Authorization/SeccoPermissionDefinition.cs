namespace Secco.SharedKernel.Authorization;

/// <summary>Permissão declarada por um produto, com a descrição mostrada a quem administra perfis (ADR-0039).</summary>
/// <param name="Name">
/// Nome canônico <c>recurso:acao</c> OU família <c>prefixo-{param}:acao</c> (ADR-0043)
/// (<see cref="SeccoPermissions.IsValid"/> / <see cref="SeccoPermissions.IsValidFamily"/>).
/// </param>
/// <param name="Description">Descrição curta em linguagem de negócio.</param>
public sealed record SeccoPermissionDefinition(string Name, string Description);
