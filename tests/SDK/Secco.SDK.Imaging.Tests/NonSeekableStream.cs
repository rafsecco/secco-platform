namespace Secco.SDK.Imaging.Tests;

/// <summary>Fluxo sem <c>Length</c> nem <c>Seek</c>, como um corpo de requisição em trânsito.</summary>
internal sealed class NonSeekableStream(byte[] content) : Stream
{
	private readonly MemoryStream _inner = new(content);

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override void Flush()
	{
	}

	public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		=> _inner.ReadAsync(buffer, cancellationToken);

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_inner.Dispose();
		}

		base.Dispose(disposing);
	}
}
