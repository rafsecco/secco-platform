namespace Secco.SDK.Imaging.Internal;

/// <summary>
/// Lê o fluxo com teto rígido de bytes. Nunca confia em <c>Stream.Length</c> (ausente ou
/// forjável): conta o que de fato chega e para em <c>limite + 1</c>.
/// </summary>
internal static class BoundedInputReader
{
	private const int ChunkSize = 81_920;

	/// <summary>Devolve os bytes lidos, ou <c>null</c> se a entrada passa de <paramref name="maxBytes"/>.</summary>
	internal static async Task<byte[]?> ReadAsync(Stream input, long maxBytes, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		var chunk = new byte[ChunkSize];
		long total = 0;

		while (true)
		{
			var read = await input.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);

			if (read == 0)
			{
				return buffer.ToArray();
			}

			total += read;

			if (total > maxBytes)
			{
				return null;
			}

			buffer.Write(chunk, 0, read);
		}
	}
}
