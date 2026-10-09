namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>Verbos de linha de comando dos binários da plataforma (ADR-0038).</summary>
public static class SeccoCommands
{
	/// <summary>Verbo que aplica migrations e seed de referência e sai, sem subir o servidor.</summary>
	public const string Migrate = "migrate";

	/// <summary>Indica se o processo foi chamado como <c>migrate</c> (primeiro argumento, exato).</summary>
	/// <param name="args">Argumentos do processo.</param>
	public static bool IsMigrate(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
		return args.Length > 0 && string.Equals(args[0], Migrate, StringComparison.Ordinal);
	}
}
