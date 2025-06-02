using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using NetCord.Gateway.Voice;

namespace MusicBot.Services;

public class AudioService(ILogger<AudioService> logger)
{
    private const string AudioFormat = "s16le";     // Using S16LE
    private const string AudioCodec = "pcm_s16le";  // Standard Discord PCM format
    private const int DiscordSampleRate = 48000;    // Standard Discord sample rate
    private const int DiscordChannels = 2;          // Standard Discord channels (stereo)
    
    internal bool Looping { get; set; }

    internal async Task StartAudioStream(Stream inStream, OpusEncodeStream outStream, CancellationToken stopToken)
    {
        logger.LogInformation("Beginning audio stream processing.");
        var loopCount = 0;
        try
        {
            do
            {
                loopCount++;
                logger.LogInformation("Starting song playback iteration {LoopCount} with FFmpeg.", loopCount);

                if (loopCount > 1)
                {
                    if (inStream.CanSeek)
                    {
                        logger.LogDebug("Seeking input stream to beginning for loop.");
                        inStream.Seek(0, SeekOrigin.Begin);
                    }
                    else
                    {
                        logger.LogWarning("Stream does not support seeking. Cannot loop properly.");
                        Looping = false; // Prevent further loops if seeking fails
                        break;
                    }
                }
                
                // Write the input stream to a temporary file
                var tempFileIn = Path.GetTempFileName();
                var tempFileOut = Path.GetTempFileName();
                await using (var fileStream = new FileStream(tempFileIn, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    logger.LogDebug("Writing input stream to temporary file: {TempFile}", tempFileIn);
                    await inStream.CopyToAsync(fileStream, stopToken);
                }
                
                await StartPcmStream(tempFileIn, tempFileOut, stopToken);
                
                // Read the PCM output file and write to Discord Opus stream
                logger.LogDebug("Reading PCM output file: {TempFile}", tempFileOut);
                await using (var pcmStream = new FileStream(tempFileOut, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    logger.LogDebug("Writing PCM data to Discord Opus stream.");
                    await pcmStream.CopyToAsync(outStream, stopToken);
                }
                
                logger.LogDebug("Flushing Discord audio stream.");
                await outStream.FlushAsync(stopToken);
                
                // Delete temporary files
                logger.LogDebug("Deleting temporary files: {TempFileIn}, {TempFileOut}", tempFileIn, tempFileOut);
                try
                {
                    File.Delete(tempFileIn);
                    File.Delete(tempFileOut);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete temporary files: {TempFileIn}, {TempFileOut}", tempFileIn, tempFileOut);
                }
                
            } while (Looping && !stopToken.IsCancellationRequested);

            logger.LogInformation("Audio stream processing finished after {Count} iteration(s). Loop requested: {Looping}, Cancelled: {Cancelled}",
                loopCount, Looping, stopToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Audio stream playback was cancelled by request.");
        }
    }
    
    private async Task StartPcmStream(string inPath, string outPath, CancellationToken stopToken)
    {
        logger.LogInformation("Starting FFmpeg process: Input -> PCM (Format: {Format}, Codec: {Codec}, Rate: {Rate}Hz, Channels: {Channels}) -> Output",
            AudioFormat, AudioCodec, DiscordSampleRate, DiscordChannels);

        try
        {
            void FfmpegErrorHandler(string msg) => logger.LogWarning("FFmpeg message: {ErrorMessage}", msg);

            await FFMpegArguments
                .FromFileInput(inPath)
                .OutputToFile(outPath, addArguments: arguments => arguments
                    .WithAudioCodec(AudioCodec)
                    .ForceFormat(AudioFormat)
                    .WithAudioSamplingRate(DiscordSampleRate)
                    .WithCustomArgument($"-ac {DiscordChannels} -af volume=-10dB")
                    .WithFastStart())
                .CancellableThrough(stopToken)
                .NotifyOnError(FfmpegErrorHandler)
                .NotifyOnOutput(FfmpegErrorHandler)
                .WithLogLevel(FFMpegLogLevel.Warning)
                .ProcessAsynchronously();
            logger.LogInformation("Finished FFmpeg audio processing pipeline.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("FFMpeg process was cancelled by request.");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FFmpeg processing failed.");
            throw;
        }
    }
}