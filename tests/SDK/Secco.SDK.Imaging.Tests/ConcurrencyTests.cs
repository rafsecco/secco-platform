using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.SDK.Imaging.Internal;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SDK.Imaging.Tests;

public sealed class ConcurrencyTests
{
	private static readonly ImageProcessingRequest Request = new([new ImageVariant("v", 64)]);

	private static SeccoImageProcessor Processor(IImageCodec codec)
		=> new(
			codec,
			Options.Create(new SeccoImagingOptions { MaxConcurrentOperations = 1 }),
			NullLogger<SeccoImageProcessor>.Instance);

	private static Task<Result<ProcessedImage>> Run(SeccoImageProcessor processor, CancellationToken cancellationToken = default)
		=> processor.ProcessAsync(new MemoryStream([1, 2, 3]), Request, cancellationToken);

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);

		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				throw new TimeoutException("Condicao nao atingida a tempo.");
			}

			await Task.Delay(10);
		}
	}

	[Fact]
	public async Task Segunda_chamada_espera_a_primeira()
	{
		using var gate = new ManualResetEventSlim(false);
		var codec = new BlockingCodec(gate);
		using var processor = Processor(codec);

		var first = Task.Run(() => Run(processor));
		await WaitUntilAsync(() => codec.RenderCalls == 1);

		var second = Task.Run(() => Run(processor));
		await Task.Delay(200);

		codec.RenderCalls.Should().Be(1, "a vaga única está ocupada pela primeira chamada");

		gate.Set();
		var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

		results.Should().OnlyContain(r => r.IsSuccess);
		codec.RenderCalls.Should().Be(2);
	}

	[Fact]
	public async Task Cancelamento_na_fila_lanca_e_nao_renderiza()
	{
		using var gate = new ManualResetEventSlim(false);
		var codec = new BlockingCodec(gate);
		using var processor = Processor(codec);

		var first = Task.Run(() => Run(processor));
		await WaitUntilAsync(() => codec.RenderCalls == 1);

		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
		var queued = () => Run(processor, cts.Token);

		await queued.Should().ThrowAsync<OperationCanceledException>();
		codec.RenderCalls.Should().Be(1);

		gate.Set();
		(await first.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task Vaga_liberada_apos_falha()
	{
		using var gate = new ManualResetEventSlim(true);
		var codec = new BlockingCodec(gate) { FailFirstRender = true };
		using var processor = Processor(codec);

		var failing = () => Run(processor);
		await failing.Should().ThrowAsync<InvalidOperationException>();

		var second = await Run(processor).WaitAsync(TimeSpan.FromSeconds(5));

		second.IsSuccess.Should().BeTrue();
		codec.RenderCalls.Should().Be(2);
	}

	private sealed class BlockingCodec(ManualResetEventSlim gate) : IImageCodec
	{
		private int _renderCalls;

		public int RenderCalls => Volatile.Read(ref _renderCalls);

		public bool FailFirstRender { get; init; }

		public Result<ImageHeader> Probe(ReadOnlyMemory<byte> data) => new ImageHeader(ImageInputFormat.Png, 10, 10);

		public Result<IReadOnlyList<RenderedImage>> Render(
			ReadOnlyMemory<byte> data,
			CropRectangle crop,
			IReadOnlyList<ImageVariant> variants,
			ImageOutputFormat format,
			int quality,
			CancellationToken cancellationToken)
		{
			var call = Interlocked.Increment(ref _renderCalls);

			if (FailFirstRender && call == 1)
			{
				throw new InvalidOperationException("falha simulada");
			}

			gate.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);

			IReadOnlyList<RenderedImage> images = [new RenderedImage("v", [1], 1, 1)];
			return Result<IReadOnlyList<RenderedImage>>.Success(images);
		}
	}
}
