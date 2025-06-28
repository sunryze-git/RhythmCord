using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using MusicBot.Exceptions;
using NetCord.Gateway.Voice;

namespace MusicBot.Services.Audio;

public class AudioServiceNative(ILogger<AudioServiceNative> logger)
{
    internal bool Looping { get; set; }
    private readonly Lock _positionLock = new();
    private TimeSpan _currentSongLength;
    private TimeSpan _currentSongPosition;
    private DateTime _playbackStartTime;
    private TimeSpan _totalFrameDuration;
    
    internal float Volume { get; set; } = 0.5f;

    internal TimeSpan CurrentSongLength 
    { 
        get { lock (_positionLock) return _currentSongLength; }
        private set { lock (_positionLock) _currentSongLength = value; }
    }

    internal TimeSpan CurrentSongPosition 
    { 
        get { lock (_positionLock) return _currentSongPosition; }
        private set { lock (_positionLock) _currentSongPosition = value; }
    }
    
    private static readonly unsafe avio_alloc_context_read_packet ReadPacketDelegate = ReadPacketCallback;
    
    private readonly byte[] _reusableBuffer = new byte[192000];
    private static readonly ThreadLocal<byte[]> CallbackBuffer = new(() => new byte[65536]);

    internal async Task StartAudioStreamAsync(Stream inStream, OpusEncodeStream outStream, CancellationToken stopToken)
    {
        if (inStream == null)
        {
            throw new ArgumentNullException(nameof(inStream), "Input stream cannot be null.");
        }
        
        if (outStream == null)
        {
            throw new ArgumentNullException(nameof(outStream), "Output stream cannot be null.");
        }
        
        if (!inStream.CanRead)
        {
            throw new ArgumentException("Input stream must be readable.", nameof(inStream));
        }
        
        logger.LogInformation("Beginning native audio stream processing.");
        var loopCount = 0;
        try
        {
            do
            {
                loopCount++;
                logger.LogInformation("Starting song playback iteration {LoopCount} with native bindings.", loopCount);

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
                ConvertToPcm(inStream, outStream, stopToken);
                
                logger.LogDebug("Flushing Discord audio stream.");
                await outStream.FlushAsync(stopToken);
                
            } while (Looping && !stopToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Audio stream playback was cancelled by request.");
        }
    }
    
    private void ConvertToPcm(Stream inStream, Stream outStream, CancellationToken token)
    {
        unsafe
        {
            AVFormatContext* formatCtx = null;
            AVCodecContext* codecCtx = null;
            SwrContext* swrCtx = null;
            AVFrame* frame = null;
            AVPacket* packet = null;
            AVIOContext* avioCtx = null;
            AVChannelLayout outLayout = default;
            byte** convertedData = null;
            byte* buffer = null;

            var inHandle = GCHandle.Alloc(inStream);
            try
            {
                _playbackStartTime = DateTime.UtcNow;
                _totalFrameDuration = TimeSpan.Zero;
                
                const int bufferSize = 4096;
                buffer = (byte*)ffmpeg.av_malloc(bufferSize);
                if (buffer == null)
                    throw new OutOfMemoryException("Failed to allocate IO buffer.");

                formatCtx = ffmpeg.avformat_alloc_context();
                if (formatCtx == null) 
                    throw new OutOfMemoryException("Failed to allocate format context.");

                avioCtx = ffmpeg.avio_alloc_context(
                    buffer,
                    bufferSize,
                    0,
                    (void*)GCHandle.ToIntPtr(inHandle),
                    ReadPacketDelegate,
                    null,
                    null);
                if (avioCtx == null)
                    throw new OutOfMemoryException("Failed to allocate AVIOContext.");

                buffer = null; // Prevent double free
                
                formatCtx->pb = avioCtx;
                formatCtx->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

                if (ffmpeg.avformat_open_input(&formatCtx, null, null, null) != 0)
                {
                    formatCtx = null;
                    var errorMsg = GetFFmpegErrorString(ffmpeg.AVERROR(ffmpeg.EINVAL));
                    throw new ApplicationException($"Failed to open input stream: {errorMsg}");
                }

                if (ffmpeg.avformat_find_stream_info(formatCtx, null) < 0)
                {
                    var errorMsg = GetFFmpegErrorString(ffmpeg.AVERROR(ffmpeg.EINVAL));
                    throw new ApplicationException($"Failed to analyze stream information: {errorMsg}");
                }
                
                // Find audio stream
                var audioStreamIndex = -1;
                for (var i = 0; i < formatCtx->nb_streams; i++)
                {
                    if (formatCtx->streams[i]->codecpar->codec_type != AVMediaType.AVMEDIA_TYPE_AUDIO) continue;
                    audioStreamIndex = i;
                    break;
                }

                if (audioStreamIndex == -1)
                {
                    throw new ApplicationException("The given content has no audio track.");
                }
                
                // Set duration
                try
                {
                    if (formatCtx->duration != ffmpeg.AV_NOPTS_VALUE)
                    {
                        // Duration is available, convert it to TimeSpan
                        CurrentSongLength = TimeSpan.FromSeconds(formatCtx->duration / (double)ffmpeg.AV_TIME_BASE);
                    }
                    else
                    {
                        var stream = formatCtx->streams[audioStreamIndex];
                        if (stream->duration != ffmpeg.AV_NOPTS_VALUE)
                        {
                            // duration is in the stream
                            var timeBase = stream->time_base;
                            CurrentSongLength = TimeSpan.FromSeconds(stream->duration * ffmpeg.av_q2d(timeBase));
                        }
                        else
                        {
                            // No duration information available, set to zero
                            logger.LogWarning("Audio stream does not contain duration information.");
                            CurrentSongLength = TimeSpan.Zero;
                        }
                    }
                }
                catch (OverflowException)
                {
                    CurrentSongLength = TimeSpan.Zero;
                }
                catch (ArgumentException)
                {
                    CurrentSongLength = TimeSpan.Zero;
                }

                var codecPar = formatCtx->streams[audioStreamIndex]->codecpar;
                var codec = ffmpeg.avcodec_find_decoder(codecPar->codec_id);
                if (codec == null)
                {
                    throw new InvalidAudioException(
                        "Unsupported audio codec.",codecPar->codec_id.ToString());
                }

                codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                if (ffmpeg.avcodec_parameters_to_context(codecCtx, codecPar) < 0)
                    throw new ApplicationException("Failed to copy codec parameters.");
                if (ffmpeg.avcodec_open2(codecCtx, codec, null) < 0)
                {
                    throw new InvalidAudioException(
                        "Failed to open codec.", codecPar->codec_id.ToString());
                }

                // Print codec information
                Console.WriteLine($"""
                                   Incoming audio stream:
                                        Codec Name: {ffmpeg.avcodec_get_name(codecPar->codec_id)}
                                        Codec ID: {codecPar->codec_id}
                                        Channel Count: {codecPar->ch_layout.nb_channels}
                                        Sample Rate: {codecPar->sample_rate}
                                        Sample Format: {codecPar->format}
                                        Duration: {CurrentSongLength:g}
                                   """);

                swrCtx = ffmpeg.swr_alloc();
                var inLayout = codecCtx->ch_layout;
                
                var stereoLayoutResult = ffmpeg.av_channel_layout_from_mask(&outLayout, ffmpeg.AV_CH_LAYOUT_STEREO);
                if (stereoLayoutResult < 0)
                {
                    throw new ApplicationException("Failed to create a stereo channel layout.");
                }

                ffmpeg.av_channel_layout_default(&outLayout, codecCtx->ch_layout.nb_channels);

                // Resampler
                const int targetSampleRate = 48000; // Discord's standard sample rate
                var res = ffmpeg.swr_alloc_set_opts2(
                    &swrCtx,
                    &outLayout,
                    AVSampleFormat.AV_SAMPLE_FMT_S16,
                    targetSampleRate,
                    &inLayout,
                    codecCtx->sample_fmt,
                    codecCtx->sample_rate,
                    0,
                    null);
                if (res < 0)
                {
                    throw new ApplicationException("Failed to allocate resample context.");
                }

                if (ffmpeg.swr_init(swrCtx) < 0)
                    throw new ApplicationException("Failed to initialize resampler.");

                packet = ffmpeg.av_packet_alloc();
                frame = ffmpeg.av_frame_alloc();
                convertedData = null;
                var maxSamples = 0;

                while (true)
                {
                    var readResult = ffmpeg.av_read_frame(formatCtx, packet);
                    if (readResult < 0) break;
                    
                    token.ThrowIfCancellationRequested();

                    if (packet->stream_index != audioStreamIndex)
                    {
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    var sendResult = ffmpeg.avcodec_send_packet(codecCtx, packet);
                    ffmpeg.av_packet_unref(packet);
                    if (sendResult < 0)
                    {
                        var errorMsg = GetFFmpegErrorString(sendResult);
                        throw new ApplicationException(
                            $"Audio decoding failed: {errorMsg}");
                    }
                    
                    while (true)
                    {
                        var receiveResult = ffmpeg.avcodec_receive_frame(codecCtx, frame);
                        if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveResult == ffmpeg.AVERROR_EOF)
                            break;
                        if (receiveResult < 0) 
                        {
                            var errorMsg = GetFFmpegErrorString(receiveResult);
                            throw new ApplicationException(
                                $"Failed to receive frame from decoder: {errorMsg}");
                        }
                        
                        token.ThrowIfCancellationRequested();

                        var outSamplesLong = ffmpeg.av_rescale_rnd(
                            ffmpeg.swr_get_delay(swrCtx, codecCtx->sample_rate) + frame->nb_samples,
                            codecCtx->sample_rate,
                            codecCtx->sample_rate,
                            AVRounding.AV_ROUND_UP);

                        if (outSamplesLong > int.MaxValue)
                        {
                            throw new OverflowException("Output samples count exceeds maximum integer value.");
                        }

                        var outSamples = (int)outSamplesLong;

                        if (convertedData == null || outSamples > maxSamples)
                        {
                            if (convertedData != null)
                                ffmpeg.av_freep(&convertedData[0]);
                            ffmpeg.av_freep(&convertedData);

                            var ret = ffmpeg.av_samples_alloc_array_and_samples(
                                &convertedData,
                                null,
                                outLayout.nb_channels,
                                outSamples,
                                AVSampleFormat.AV_SAMPLE_FMT_S16,
                                0);

                            if (ret < 0)
                                throw new ApplicationException("Failed to allocate sample buffer.");

                            maxSamples = outSamples;
                        }

                        var convertedSamples = ffmpeg.swr_convert(
                            swrCtx,
                            convertedData,
                            outSamples,
                            frame->extended_data,
                            frame->nb_samples);

                        // If there are no samples to convert, continue
                        if (outSamples <= 0 || convertedData == null)
                        {
                            Console.WriteLine("No samples converted, skipping to next packet.");
                            continue;
                        }

                        if (convertedSamples < 0)
                            throw new ApplicationException("Error during resampling.");

                        var outputBufferSize = ffmpeg.av_samples_get_buffer_size(
                            null,
                            outLayout.nb_channels,
                            convertedSamples,
                            AVSampleFormat.AV_SAMPLE_FMT_S16,
                            1);

                        if (outputBufferSize < 0)
                            throw new ApplicationException("Failed to calculate output buffer size.");

                        token.ThrowIfCancellationRequested();
                        byte[] managedBuffer;
                        if (outputBufferSize <= _reusableBuffer.Length)
                        {
                            managedBuffer = _reusableBuffer;
                        }
                        else
                        {
                            logger.LogDebug("Buffer size exceeded reusable buffer, allocating new buffer of size {OutputBufferSize}.", outputBufferSize);
                            managedBuffer = new byte[outputBufferSize];
                        }
                        
                        Marshal.Copy((IntPtr)convertedData[0], managedBuffer, 0, outputBufferSize);

                        // Apply a volume adjustment
                        var sampleCount = outputBufferSize / 2;
                        fixed (byte* bufferPtr = managedBuffer)
                        {
                            var samplePtr = (short*)bufferPtr;
                            var volumeFactor = Volume;
                            for (var i = 0; i < sampleCount; i++)
                            {
                                var scaled = (int)(samplePtr[i] * volumeFactor);

                                scaled = scaled switch
                                {
                                    // Clamp to 16-bit signed
                                    > short.MaxValue => short.MaxValue,
                                    < short.MinValue => short.MinValue,
                                    _ => scaled
                                };
                                samplePtr[i] = (short)scaled;
                            }
                        }
                        
                        // hacky ass fix
                        var frameDuration = TimeSpan.FromSeconds((double)convertedSamples / targetSampleRate);
                        _totalFrameDuration += frameDuration;

                        var expectedElapsed = _totalFrameDuration;
                        var actualElapsed = DateTime.UtcNow - _playbackStartTime;

                        if (expectedElapsed > actualElapsed)
                        {
                            var delay = expectedElapsed - actualElapsed;
                            var delayMicroseconds = (int)(delay.TotalMilliseconds * 1000);
                            if (delayMicroseconds > 1000) // Only delay if > 1ms
                            {
                                ffmpeg.av_usleep((uint)delayMicroseconds);
                            }
                        }

                        outStream.Write(managedBuffer, 0, outputBufferSize);
                        
                        // Update current song position
                        try
                        {
                            var timeBase = formatCtx->streams[audioStreamIndex]->time_base;
                            CurrentSongPosition = TimeSpan.FromSeconds(frame->pts * ffmpeg.av_q2d(timeBase));
                        }
                        catch (OverflowException)
                        {
                            CurrentSongPosition = TimeSpan.MaxValue; // Handle overflow gracefully
                        }
                        catch (ArgumentException)
                        {
                            CurrentSongPosition = TimeSpan.Zero;
                        }
                    }

                    ffmpeg.av_packet_unref(packet);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Audio conversion was cancelled by request.");
                throw;
            }
            finally
            {
                ffmpeg.av_channel_layout_uninit(&outLayout);
                
                if (convertedData != null)
                {
                    if (convertedData[0] != null)
                        ffmpeg.av_freep(&convertedData[0]);
                    ffmpeg.av_freep(&convertedData);
                }

                if (packet != null)
                {
                    ffmpeg.av_packet_unref(packet);
                    ffmpeg.av_packet_free(&packet);
                }
                
                if (frame != null) ffmpeg.av_frame_free(&frame);
                if (swrCtx != null) ffmpeg.swr_free(&swrCtx);
                if (codecCtx != null) ffmpeg.avcodec_free_context(&codecCtx);

                // AVIO context cleanup - this also frees the buffer
                if (avioCtx != null)
                {
                    ffmpeg.avio_context_free(&avioCtx);
                    buffer = null; // Buffer was freed by avio_context_free
                }
    
                // Only free buffer if AVIO context wasn't created
                if (buffer != null)
                {
                    ffmpeg.av_free(buffer);
                }
                
                // Format context cleanup
                if (formatCtx != null) 
                    ffmpeg.avformat_close_input(&formatCtx);
                
                // Dispose stream before freeing GCHandle
                try
                {
                    inStream.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing input stream");
                }

                // Free GCHandle last
                if (inHandle.IsAllocated)
                    inHandle.Free();

                // Reset position tracking
                CurrentSongLength = TimeSpan.Zero;
                CurrentSongPosition = TimeSpan.Zero;

                Console.WriteLine("Audio stream processing completed and resources cleaned up.");
            }
        }
    }

    private static unsafe string GetFFmpegErrorString(int averror)
    {
        var buffer = stackalloc byte[256];
        if (ffmpeg.av_strerror(averror, buffer, 256) < 0)
        {
            return $"Unknown error code: {averror}";
        }
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"Unknown error code: {averror}";
    }

    private static unsafe int ReadPacketCallback(void* opaque, byte* buf, int bufSize)
    {
        try
        {
            var handle = GCHandle.FromIntPtr((IntPtr)opaque);
            if (!handle.IsAllocated || handle.Target is not Stream inStream)
                return ffmpeg.AVERROR(ffmpeg.EINVAL);

            var buffer = CallbackBuffer.Value;
            if (buffer?.Length < bufSize)
            {
                buffer = new byte[Math.Max(bufSize, 65536)];
                CallbackBuffer.Value = buffer;
            }

            var bytesRead = inStream.Read(buffer!, 0, bufSize);

            if (bytesRead == 0)
                return ffmpeg.AVERROR_EOF;

            Marshal.Copy(buffer!, 0, (IntPtr)buf, bytesRead);
            return bytesRead;
        }
        catch (ObjectDisposedException)
        {
            return ffmpeg.AVERROR_EOF;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] ReadPacketCallback: {ex.Message}");
            return ffmpeg.AVERROR(ffmpeg.EINVAL);
        }
    }
}