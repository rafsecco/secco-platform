namespace Secco.SDK.Documents.Internal;

/// <summary>
/// Arquivos temporários da exportação (ADR-0058). Guardam dado de tenant, então: diretório próprio
/// só do dono (0700) e arquivos 0600 no Linux, apagados ao descartar, e restos de um processo que
/// caiu apagados na subida — no Linux, o <c>DeleteOnClose</c> só apaga quando o arquivo fecha.
/// </summary>
internal sealed class TempFileStore
{
	internal const string DirectoryName = "secco-documents";

	/// <summary>Nenhuma exportação dura isso: arquivo mais velho é resto de processo que caiu.</summary>
	private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

	private readonly string _directory;

	internal TempFileStore(string? root, TimeProvider time)
	{
		_directory = Path.Combine(string.IsNullOrWhiteSpace(root) ? Path.GetTempPath() : root, DirectoryName);
		EnsureDirectory();
		Sweep(time.GetUtcNow());
	}

	internal string Directory => _directory;

	internal FileStream Create()
	{
		var options = new FileStreamOptions
		{
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.None,
			Options = FileOptions.DeleteOnClose,
		};

		if (!OperatingSystem.IsWindows())
		{
			options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
		}

		return new FileStream(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp"), options);
	}

	private void EnsureDirectory()
	{
		if (OperatingSystem.IsWindows())
		{
			System.IO.Directory.CreateDirectory(_directory);
			return;
		}

		System.IO.Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		// No /tmp compartilhado, outro usuário poderia ter criado o diretório antes (ou um link para
		// outro lugar) para ler as exportações. Só segue se for diretório de verdade e só do dono.
		var info = new DirectoryInfo(_directory);
		var mode = File.GetUnixFileMode(_directory);
		var groupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
			| UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

		if (info.LinkTarget is not null || (mode & groupOrOther) != 0)
		{
			throw new InvalidOperationException(
				$"O diretório temporário de exportação '{_directory}' é link ou acessível a outros usuários; corrija ou configure TempDirectory.");
		}
	}

	private void Sweep(DateTimeOffset now)
	{
		foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.tmp"))
		{
			try
			{
				if (now - File.GetLastWriteTimeUtc(file) > StaleAfter)
				{
					File.Delete(file);
				}
			}
			catch (IOException)
			{
				// Em uso por outro processo (Windows): não é resto.
			}
			catch (UnauthorizedAccessException)
			{
				// Arquivo de outro usuário: não é nosso para apagar.
			}
		}
	}
}
