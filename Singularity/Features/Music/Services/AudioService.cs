using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Features.Music.Services;

internal sealed class AudioDecodingException(string message) : Exception(message);

/// <summary>Decodes a media stream with FFmpeg and feeds it to Discord. One instance per guild.</summary>
internal class AudioService(ILogger<AudioService> logger, MusicOptions options, BotMetrics metrics)
{
    // EBU R128-style loudness normalisation so quiet and loud tracks play at a similar volume (Music:Normalize).
    private const string LoudnessFilter = "loudnorm=I=-16:TP=-1.5:LRA=11";

    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int FrameDurationMs = 20;
    private const int FrameSize = SampleRate * Channels * sizeof(short) * FrameDurationMs / 1000;
    private const double BytesPerSecond = SampleRate * Channels * sizeof(short);

    private long _consumedBytes;

    public TimeSpan Position =>
        TimeSpan.FromTicks((long)(Volatile.Read(ref _consumedBytes) / BytesPerSecond * TimeSpan.TicksPerSecond));

    /// <summary>Plays the stream once. Cancelling 'stopToken' ends playback quietly (no exception).</summary>
    internal async Task StartAudioStreamAsync(Stream inStream, Stream outStream, CancellationToken stopToken)
    {
        ArgumentNullException.ThrowIfNull(inStream);
        ArgumentNullException.ThrowIfNull(outStream);

        if (!inStream.CanRead)
            throw new ArgumentException("Input stream must be readable.", nameof(inStream));

        logger.LogDebug("Beginning audio stream processing via FFmpeg CLI.");

        try
        {
            await ConvertToPcmAsync(inStream, outStream, stopToken);
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            logger.LogDebug("Audio stream playback was cancelled by request.");
        }
        finally
        {
            logger.LogDebug("Flushing Discord audio stream.");
            try
            {
                await outStream.FlushAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to flush Discord audio stream.");
            }
        }
    }

    private async Task ConvertToPcmAsync(Stream inputStream, Stream outputStream, CancellationToken token)
    {
        Interlocked.Exchange(ref _consumedBytes, 0); // reset position per song/loop iteration

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-i", "pipe:0" };
        if (options.Normalize) args.AddRange(["-af", LoudnessFilter]);
        args.AddRange(["-f", "s16le", "-ar", "48000", "-ac", "2", "pipe:1"]);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start FFmpeg process.");
        using var ffmpegActive = metrics.TrackFfmpeg();

        // Cancelling this tears down both pumps as soon as either one fails.
        using var pipeCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        var errorTask = process.StandardError.ReadToEndAsync();
        errorTask.Observe();

        var writeTask = Task.Run(() => GuardAsync(() => PumpInputAsync(inputStream, process, pipeCts.Token), pipeCts));
        var readTask = Task.Run(() => GuardAsync(() => PumpOutputAsync(process, outputStream, pipeCts.Token), pipeCts));
        var pumps = Task.WhenAll(writeTask, readTask);

        try
        {
            await pumps;
            await process.WaitForExitAsync(token);
        }
        catch (Exception ex)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);

            // One pump failing cancels the other; surface the real failure, not the induced cancellation.
            if (!token.IsCancellationRequested && ex is OperationCanceledException &&
                pumps.Exception?.InnerExceptions.FirstOrDefault(e => e is not OperationCanceledException) is { } real)
                ExceptionDispatchInfo.Capture(real).Throw();

            throw;
        }

        if (process.ExitCode != 0)
        {
            var errorLog = await errorTask;
            throw new AudioDecodingException($"FFmpeg process exited with code {process.ExitCode}: {errorLog}");
        }
    }

    private static async Task GuardAsync(Func<Task> work, CancellationTokenSource pipeCts)
    {
        try
        {
            await work();
        }
        catch
        {
            pipeCts.Cancel();
            throw;
        }
    }

    private static async Task PumpInputAsync(Stream inputStream, Process process, CancellationToken token)
    {
        try
        {
            await inputStream.CopyToAsync(process.StandardInput.BaseStream, token);
        }
        catch (Exception ex) when (process.HasExited || ex is OperationCanceledException)
        {
            // Ignore pipe breakage if process terminated early or was cancelled
        }
        finally
        {
            try
            {
                process.StandardInput.BaseStream.Close();
            }
            catch (IOException)
            {
                // FFmpeg already closed its end.
            }
        }
    }

    // Read stdout with exact 20ms frame boundaries
    private async Task PumpOutputAsync(Process process, Stream outputStream, CancellationToken token)
    {
        var buffer = new byte[FrameSize];
        var stdout = process.StandardOutput.BaseStream;

        while (!token.IsCancellationRequested)
        {
            var filled = 0;
            while (filled < FrameSize)
            {
                var read = await stdout.ReadAsync(buffer.AsMemory(filled, FrameSize - filled), token);
                if (read == 0) break; // EOF reached
                filled += read;
            }

            if (filled == 0) break;

            if (filled < FrameSize)
                Array.Clear(buffer, filled, FrameSize - filled);

            Interlocked.Add(ref _consumedBytes, filled);
            metrics.AudioBytes(filled);
            await outputStream.WriteAsync(buffer.AsMemory(0, FrameSize), token);
        }
    }
}
