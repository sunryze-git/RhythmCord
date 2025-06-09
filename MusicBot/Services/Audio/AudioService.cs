using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using NetCord.Gateway.Voice;

namespace MusicBot.Services.Audio;

public class AudioService(ILogger<AudioService> logger)
{
    private const string AudioFormat = "s16le";     // Using S16LE
    private const string AudioCodec = "pcm_s16le";  // Standard Discord PCM format
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
                await ConvertToPcmAsync(inStream, outStream, stopToken);
                
                logger.LogDebug("Flushing Discord audio stream.");
                await outStream.FlushAsync(stopToken);
                
            } while (Looping && !stopToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Audio stream playback was cancelled by request.");
        }
    }
    
    private async Task ConvertToPcmAsync(Stream inStream, Stream outStream, CancellationToken stopToken)
    {
        try
        {
            // Generate temporary files for the input stream and output stream
            var inPath = Path.GetTempFileName();
            var outPath = Path.GetTempFileName();
            
            // Copy the input stream to the temporary file
            await using var inputFile = new FileStream(inPath, FileMode.Create, FileAccess.ReadWrite);
            await inStream.CopyToAsync(inputFile, stopToken);

            // Transcode the audio using FFmpeg
            await FFMpegArguments
                .FromFileInput(inPath)
                .OutputToPipe(new StreamPipeSink(outStream), addArguments: arguments => arguments
                    .WithAudioCodec(AudioCodec)
                    .ForceFormat(AudioFormat)
                    .WithAudioSamplingRate()
                    .WithCustomArgument($"-ac {DiscordChannels} -af volume=-10dB")
                    .WithFastStart())
                .CancellableThrough(stopToken)
                .NotifyOnError(msg => logger.LogWarning("FFMPEG: {Message}", msg))
                .WithLogLevel(FFMpegLogLevel.Warning)
                .ProcessAsynchronously();
            logger.LogInformation("Finished FFmpeg audio processing pipeline.");

            // Write the processed audio to the output stream
            await using var outputFile = new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite);
            await outputFile.CopyToAsync(outStream, stopToken);
            
            // Delete temporary files
            File.Delete(inPath);
            File.Delete(outPath);
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