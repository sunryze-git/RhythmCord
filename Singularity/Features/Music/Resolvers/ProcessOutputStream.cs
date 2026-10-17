using System.Diagnostics;

namespace Singularity.Features.Music.Resolvers;

/// <summary>
///     Read-only, non-seekable view over a child process's stdout. Disposing it kills and disposes the process, so
///     skipping a track never leaves the downloader running. If the process ends with a failure, the end of the stream
///     is reported as a <see cref="MediaStreamException" /> carrying the tool's own error message, rather than as a
///     silent short track that the audio decoder would then misreport.
/// </summary>
internal sealed class ProcessOutputStream : Stream
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(5);

    private readonly Stream _inner;
    private readonly Process _process;
    private readonly StderrTail _stderr;
    private readonly IDisposable? _onDispose;
    private int _exitChecked;

    internal ProcessOutputStream(Process process, StderrTail stderr, IDisposable? onDispose = null)
    {
        _process = process;
        _stderr = stderr;
        _onDispose = onDispose;
        _inner = process.StandardOutput.BaseStream;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        if (read == 0 && Interlocked.Exchange(ref _exitChecked, 1) == 0)
            ThrowIfFailed(_process.WaitForExit(ExitWait));

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        if (read == 0 && Interlocked.Exchange(ref _exitChecked, 1) == 0)
        {
            bool exited;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ExitWait);
            try
            {
                await _process.WaitForExitAsync(timeout.Token);
                exited = true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                exited = false;
            }

            ThrowIfFailed(exited);
        }

        return read;
    }

    private void ThrowIfFailed(bool exited)
    {
        // If it has not exited yet we cannot tell success from failure, so do not guess.
        if (exited && _process.ExitCode != 0)
            throw new MediaStreamException($"the download failed ({_stderr.Reason(_process.ExitCode)})");
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            _process.Dispose();
            _onDispose?.Dispose();
        }

        base.Dispose(disposing);
    }
}
