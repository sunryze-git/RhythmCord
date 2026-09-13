using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetCord.Gateway.Voice;

namespace MusicBot.Features.Audio;

public class AudioService(ILogger<AudioService> logger) : IAudioService
{
    private long _consumedBytes;

    public TimeSpan Position => TimeSpan.FromTicks((long)(Volatile.Read(ref _consumedBytes) / BytesPerSecond * TimeSpan.TicksPerSecond));
    public bool Looping { get; set; }

    private readonly Lock _lock = new();
    private CancellationTokenSource? _active;

    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int FrameDurationMs = 20;
    private const int FrameSize = SampleRate * Channels * sizeof(short) * FrameDurationMs / 1000;
    private const double BytesPerSecond = SampleRate * Channels * sizeof(short);

    Task IAudioService.StartAudioStreamAsync(Stream inStream, OpusEncodeStream outStream, CancellationToken stopToken, CancellationToken serviceToken)
    {
        return StartAudioStreamAsync(inStream, outStream, stopToken, serviceToken);
    }

    internal async Task StartAudioStreamAsync(Stream inStream, OpusEncodeStream outStream, CancellationToken stopToken, CancellationToken serviceToken = default)
    {
        ArgumentNullException.ThrowIfNull(inStream);
        ArgumentNullException.ThrowIfNull(outStream);

        var linked = CancellationTokenSource.CreateLinkedTokenSource(stopToken, serviceToken);
        lock (_lock) { _active = linked; }

        if (!inStream.CanRead)
            throw new ArgumentException("Input stream must be readable.", nameof(inStream));

        logger.LogInformation("Beginning audio stream processing via FFmpeg CLI.");
        var loopCount = 0;

        try
        {
            do
            {
                loopCount++;
                logger.LogInformation("Starting song playback iteration {LoopCount}.", loopCount);

                if (loopCount > 1)
                {
                    if (!inStream.CanSeek)
                    {
                        logger.LogWarning("Stream does not support seeking. Cannot loop properly.");
                        Looping = false;
                        break;
                    }
                    inStream.Seek(0, SeekOrigin.Begin);
                }

                await ConvertToPcmAsync(inStream, outStream, linked.Token);
            } while (Looping && !linked.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Audio stream playback was cancelled by request.");
        }

        logger.LogDebug("Flushing Discord audio stream.");
        await outStream.FlushAsync();
    }

    private async Task ConvertToPcmAsync(Stream inputStream, OpusEncodeStream outputStream, CancellationToken linkedToken)
    {
        _consumedBytes = 0; // reset position per song/loop iteration

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-hide_banner -loglevel error -i pipe:0 -f s16le -ar 48000 -ac 2 pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start FFmpeg process.");
        }

        // Pump stdin asynchronously
        var writeTask = Task.Run(async () =>
        {
            try
            {
                await inputStream.CopyToAsync(process.StandardInput.BaseStream, linkedToken);
            }
            catch (Exception ex) when (process.HasExited || ex is OperationCanceledException)
            {
                // Ignore pipe breakage if process terminated early or was cancelled
            }
            finally
            {
                process.StandardInput.BaseStream.Close();
            }
        }, linkedToken);

        // Read stdout with exact 20ms frame boundaries
        var readTask = Task.Run(async () =>
        {
            var buffer = new byte[FrameSize];
            var stdout = process.StandardOutput.BaseStream;

            while (!linkedToken.IsCancellationRequested)
            {
                var bytesRead = 0;
                var totalBytes = Volatile.Read(ref _consumedBytes);

                while (bytesRead < FrameSize)
                {
                    var read = await stdout.ReadAsync(buffer.AsMemory(bytesRead, FrameSize - bytesRead), linkedToken);
                    if (read == 0) break; // EOF reached
                    bytesRead += read;
                }

                if (bytesRead == 0) break;

                totalBytes += bytesRead;
                Volatile.Write(ref _consumedBytes, totalBytes);

                if (bytesRead < FrameSize)
                {
                    Array.Clear(buffer, bytesRead, FrameSize - bytesRead);
                }

                await outputStream.WriteAsync(buffer.AsMemory(0, FrameSize), linkedToken);
            }
        }, linkedToken);

        var errorTask = process.StandardError.ReadToEndAsync(linkedToken);

        try
        {
            await Task.WhenAll(writeTask, readTask);
            await process.WaitForExitAsync(linkedToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }

        if (process.ExitCode != 0)
        {
            var errorLog = await errorTask;
            throw new InvalidOperationException($"FFmpeg process exited with code {process.ExitCode}: {errorLog}");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            try
            {
                _active?.Cancel();
            }
            catch (ObjectDisposedException) { }

            _active?.Dispose();
            _active = null;
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
