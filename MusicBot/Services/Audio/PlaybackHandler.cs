using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MusicBot.Exceptions;
using MusicBot.Services.Media;
using MusicBot.Services.Queue;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Services.ApplicationCommands;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Audio;

public class PlaybackHandler(
    ILogger<PlaybackHandler> logger,
    AudioService audioService,
    QueueManager queueManager,
    MediaResolver mediaResolver,
    GatewayClient gatewayClient)
{
    // Public API methods
    public bool Active => _playbackTask?.Status is TaskStatus.Running or TaskStatus.WaitingForActivation or TaskStatus.WaitingToRun;
    public void SkipSong() => _skipSongCts?.Cancel();

    public bool ToggleLooping()
    {
        audioService.Looping = !audioService.Looping;
        return audioService.Looping;
    }
    public void Shuffle() => queueManager.Shuffle();
    public void Stop() => StopQueue();
    public async Task End() => await EndPlaybackAsync();
    public ImmutableList<IVideo> SongQueue => queueManager.SongQueue;
    public IVideo? CurrentSong => queueManager.CurrentSong;

    public async Task<IVideo> AddSongAsync(string term, bool next, ApplicationCommandContext ctx)
    {
        _invokedChannel = ctx.Channel;
        _invokedGuild = ctx.Guild;
        
        logger.LogInformation("Adding song to queue: {Term}", term);
        if (_playbackTask == null)
        {
            var targetChannel = CollectionExtensions.GetValueOrDefault(ctx.Guild!.VoiceStates, ctx.User.Id);
            _voiceClient = await ctx.Client.JoinVoiceChannelAsync(ctx.Guild.Id, targetChannel!.ChannelId.GetValueOrDefault());
        }
        
        if (_voiceClient == null) 
        {
            logger.LogError("Voice client is null. Cannot add song.");
            throw new InvalidOperationException("Voice client is not initialized.");
        }

        var songsToAdd = await mediaResolver.ResolveSongsAsync(term);
        if (songsToAdd.Count == 0)
        {
            logger.LogWarning("No songs found for term: {Term}", term);
            throw new SearchException("No songs found for the provided term.");
        }

        queueManager.AddSong(songsToAdd, next);
        StartQueue(_voiceClient);
        return songsToAdd[0];
    }
    
    // Private Things
    private Task? _playbackTask;
    private CancellationTokenSource? _stopRunnerCts;
    private CancellationTokenSource? _skipSongCts;
    private CancellationTokenSource? _inactivityCts;
    private Guild? _invokedGuild;
    private TextChannel? _invokedChannel;
    private VoiceClient? _voiceClient;
    
    private void StartQueue(VoiceClient voiceClient)
    {
        logger.LogInformation("Beginning playback of queue.");
        _inactivityCts?.Cancel();
        if (_playbackTask == null)
        {
            _playbackTask = Task.Run(() => PlaybackRunner(voiceClient));
            logger.LogInformation("Playback runner start initiated.");
        }
        else
        {
            logger.LogInformation("Playback task already running.");
        }
    }

    private void StopQueue() // used to stop playback, but remain ready for songs
    {
        queueManager.Clear();
        _skipSongCts?.Cancel(); // skipping on an empty queue triggers inactivity timer
    }

    private async Task EndPlaybackAsync() // used to stop playback and leave the voice channel
    {
        // called to end playback gracefully
        logger.LogInformation("Ending playback.");
        queueManager.Clear(); // 1. clear the queue
        try
        {
            await _stopRunnerCts!.CancelAsync(); // 2. calling stop will cancel playback on the next skip
            await _skipSongCts!.CancelAsync(); // 3. cancel any current song playback
            await _inactivityCts!.CancelAsync(); // 4. cancel inactivity, if that's where we are
            
            // Dispose will be called somewhere before this
            await _playbackTask!.WaitAsync(TimeSpan.FromMinutes(1)); // 4. wait for playback task to finish
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogError(ex, "Cancellation tokens were already disposed when attempting to cancel. Playback task has probably already finished, this is okay.");
        }
    }
    
    private void Dispose()
    {
        _playbackTask?.Wait(); // Ensure playback task is completed before disposing
        _playbackTask?.Dispose();
        _stopRunnerCts?.Dispose();
        _skipSongCts?.Dispose();
        _inactivityCts?.Dispose();
        _voiceClient?.Dispose();
    }
    
    private async Task PlaybackRunner(VoiceClient voiceClient)
    {
        try
        {
            logger.LogInformation("Playback runner has started!");
            await voiceClient.StartAsync();
            await voiceClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);

            await using var outStream = voiceClient.CreateOutputStream();
            await using var opusEncodeStream =
                new OpusEncodeStream(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

            _stopRunnerCts = new CancellationTokenSource();
            while (!_stopRunnerCts.IsCancellationRequested)
            {
                _inactivityCts?.Dispose();
                _inactivityCts = new CancellationTokenSource();
                try
                {
                    while (!queueManager.IsEmpty())
                    {
                        await PlaySong(opusEncodeStream);
                        if (_stopRunnerCts.IsCancellationRequested) break;
                    }

                    if (_stopRunnerCts.IsCancellationRequested)
                    {
                        logger.LogInformation("Playback stopped by user request.");
                        break;
                    }

                    logger.LogInformation("Queue is empty. Starting inactivity timer.");
                    if (_invokedChannel != null)
                    {
                        try
                        {
                            await _invokedChannel.SendMessageAsync("Reached the end of the queue.");
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    await Task.Delay(TimeSpan.FromMinutes(10), _inactivityCts.Token);
                    logger.LogInformation("Inactivity timer completed. Stopping playback.");
                    break;
                }
                catch (TaskCanceledException)
                {
                    logger.LogWarning("Inactivity timer was cancelled, continuing playback.");
                }
                catch (ObjectDisposedException)
                {
                    logger.LogError("Inactivity timer was disposed. Something bad happened. Stopping playback.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal exception in the playback runner.");
            if (_invokedChannel != null)
            {
                try
                {
                    await _invokedChannel.SendMessageAsync("Fatal error occurred during playback.");
                }
                catch
                {
                    // ignored
                }
            }
        }
        finally
        {
            logger.LogInformation("Playback runner has stopped.");
        }
        
        // Start a task to clean up resources after playback ends
        await gatewayClient.UpdateVoiceStateAsync(new VoiceStateProperties(_invokedGuild!.Id, null));
        await Task.Run(Dispose);
    }

    private async Task PlaySong(OpusEncodeStream outStream)
    {
        _skipSongCts?.Dispose();
        _skipSongCts = new CancellationTokenSource();

        try
        {
            var next = queueManager.CurrentSong;
            if (next == null)
            {
                logger.LogWarning("No song to play, skipping.");
                return;
            }

            await using var songStream = await mediaResolver.ResolveStreamAsync(next);
            await audioService.StartAudioStream(songStream, outStream, _skipSongCts.Token);
            
            // Remove the song from the queue after playback
            queueManager.RemoveCurrent();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Skipping song due to cancellation request.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP request failed while playing song.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unknown error occurred while playing song.");
        }
    }
}