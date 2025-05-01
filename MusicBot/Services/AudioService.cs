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
                
                await StartPcmStream(inStream, outStream, stopToken);
                
                logger.LogDebug("Flushing Discord audio stream.");
                await outStream.FlushAsync(stopToken);
                
            } while (Looping && !stopToken.IsCancellationRequested);

            logger.LogInformation("Audio stream processing finished after {Count} iteration(s). Loop requested: {Looping}, Cancelled: {Cancelled}",
                loopCount, Looping, stopToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Audio stream playback was cancelled by request.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Audio stream playback failed: {ErrorMessage}", e.Message);
            throw;
        }
    }
    
    private async Task StartPcmStream(Stream inStream, Stream outStream, CancellationToken stopToken)
    {
        logger.LogInformation("Starting FFmpeg process: Input -> PCM (Format: {Format}, Codec: {Codec}, Rate: {Rate}Hz, Channels: {Channels}) -> Output",
            AudioFormat, AudioCodec, DiscordSampleRate, DiscordChannels);

        try
        {
            void FfmpegErrorHandler(string msg) => logger.LogError("FFmpeg Error: {ErrorMessage}", msg);

            await FFMpegArguments
                .FromPipeInput(new StreamPipeSource(inStream), options => options 
                    .WithCustomArgument("-re"))
                .OutputToPipe(new StreamPipeSink(outStream), options => options
                    .ForceFormat(AudioFormat)
                    .WithAudioCodec(AudioCodec)
                    .WithAudioSamplingRate()
                    .WithAudioBitrate(AudioQuality.Low)
                    .WithCustomArgument($"-ac {DiscordChannels} -af volume=-10dB"))
                .CancellableThrough(stopToken)
                .NotifyOnError(FfmpegErrorHandler)
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