namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Quem desativou a conta (ADR-0055). Existe para a sincronização com o diretório saber o que pode desfazer: só a
/// desativação feita por ela (<see cref="Directory"/>) volta quando a pessoa reentra no grupo; a de um admin
/// (<see cref="Manual"/>) é decisão humana e a sincronização nunca a desfaz.
/// </summary>
public enum DeactivationOrigin
{
	/// <summary>A conta não está desativada (ou foi reativada).</summary>
	None,

	/// <summary>Desativada por um admin, pela API ou pela tela de gestão.</summary>
	Manual,

	/// <summary>Desativada pela sincronização com o diretório federado (saiu do grupo de provisionamento).</summary>
	Directory,
}

/// <summary>
/// Os valores de <see cref="DeactivationOrigin"/> como o contrato os expõe: texto, como <see cref="UserStatuses"/> —
/// o record posicional do contrato não carrega enum.
/// </summary>
public static class DeactivationOrigins
{
	/// <summary>Não desativada.</summary>
	public const string None = nameof(DeactivationOrigin.None);

	/// <summary>Desativada por um admin.</summary>
	public const string Manual = nameof(DeactivationOrigin.Manual);

	/// <summary>Desativada pela sincronização com o diretório.</summary>
	public const string Directory = nameof(DeactivationOrigin.Directory);

	/// <summary>Texto do contrato para a origem.</summary>
	/// <param name="origin">Origem persistida.</param>
	public static string From(DeactivationOrigin origin) => origin.ToString();
}
