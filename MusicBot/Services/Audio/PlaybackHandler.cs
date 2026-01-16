using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicBot.Exceptions;
using MusicBot.Services.Media;
using MusicBot.Services.Queue;
using MusicBot.Utilities;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Services.Audio;

public class PlaybackHandler(
    ILogger<PlaybackHandler> logger,
    AudioServiceNative audioService,
    QueueManager queueManager,
    MediaResolver mediaResolver,
    ApplicationCommandContext commandContext)
{
    // Public API methods
    public bool Active => _playbackTask?.Status is TaskStatus.Running or TaskStatus.WaitingForActivation or TaskStatus.WaitingToRun;
    public bool Initialized => _voiceClient != null;
    public void SkipSong() => _skipSongCts?.Cancel();

    public bool ToggleLooping()
    {
        audioService.Looping = !audioService.Looping;
        return audioService.Looping;
    }
    public void Shuffle() => queueManager.Shuffle();
    public void Stop() => StopQueue();
    public Task EndAsync() => LeaveVoiceAsync();

    public ImmutableList<MusicTrack> SongQueue => queueManager.SongQueue;
    public MusicTrack? CurrentSong => queueManager.CurrentSong;
    public TimeSpan Duration => audioService.CurrentSongLength;
    public TimeSpan Position => audioService.CurrentSongPosition;

    private Task? _playbackTask;
    private CancellationTokenSource? _runnerCts; // replaces _stopRunnerCts
    private CancellationTokenSource? _skipSongCts;
    private CancellationTokenSource? _inactivityCts;
    private VoiceClient? _voiceClient;
    
    // Post-Construction Initialization
    public async Task InitializeAsync()
    {
        _voiceClient = await JoinVoiceAsync();
        _voiceClient.Disconnect += ShutdownAsync;
    }
    
    // Voice State
    private async Task<VoiceClient> JoinVoiceAsync()
    {
        var target = CollectionExtensions.GetValueOrDefault(commandContext.Guild!.VoiceStates, commandContext.User.Id);
        if (target is not { ChannelId: not null })
            throw new InvalidOperationException("Could not determine user's voice channel.");
        
        return await commandContext.Client.JoinVoiceChannelAsync(commandContext.Guild.Id, target.ChannelId.Value);
    }
    
    private async Task LeaveVoiceAsync()
    {
        if (commandContext.Guild == null || _voiceClient == null) return;
        await commandContext.Client.UpdateVoiceStateAsync(new VoiceStateProperties(commandContext.Guild.Id, null));
    }

    // Start the playback loop with the given voice client
    public void StartQueue()
    {
        logger.LogInformation("Beginning playback of queue.");
        _inactivityCts?.Cancel();

        if ((_playbackTask == null || _playbackTask.IsCompleted) && _voiceClient != null)
        {
            _playbackTask = Task.Run(() => PlaybackRunnerAsync(_voiceClient));
            logger.LogInformation("Playback runner start initiated.");
        }
        else
        {
            logger.LogInformation("Playback task already running.");
        }
    }
    
    public async Task<MusicTrack> AddSongAsync(string term, bool next)
    {
        logger.LogInformation("Adding song to queue: {Term}", term);

        var songsToAdd = await mediaResolver.ResolveSongsAsync(term);
        if (songsToAdd.Count == 0)
        {
            logger.LogWarning("No songs found for term: {Term}", term);
            throw new SearchException("No songs found for the provided term.");
        }

        queueManager.AddSong(songsToAdd, next);
        return songsToAdd[0];
    }

    // Stop the queue and clear it
    private void StopQueue() 
    {
        queueManager.Clear();
        _skipSongCts?.Cancel(); // skipping on an empty queue triggers inactivity timer
    }
    
    // Shutdown and Cleanup Handling
    private async ValueTask ShutdownAsync(bool arg)
    {
        logger.LogInformation("Shutdown requested for playback handler.");

        // Clear the queue and cancel runner/skip/inactivity tokens
        queueManager.Clear();

        // Cancel all relevant tokens
        // Cancelling all these tokens will result in playback loop to end.
        try
        {
            _runnerCts?.CancelAsync(); // cancel the playback loop
            _skipSongCts?.CancelAsync(); // cancel current song playback
            _inactivityCts?.CancelAsync(); // cancel inactivity timer
        }
        catch
        {
            logger.LogError("Error cancelling playback tokens during player shutdown.");
        }

        // Wait briefly for the playback task to finish
        if (_playbackTask != null)
        {
            try
            {
                await _playbackTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
            {
                logger.LogWarning(ex, "Playback task did not complete in time during shutdown.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while waiting for playback task during shutdown.");
            }
        }

        // Perform final cleanup synchronously
        try
        {
            if (MusicBot.Services != null && commandContext.Guild != null)
            {
                var globalService = MusicBot.Services.GetRequiredService<GuildAudioInstanceOrchestrator>();
                globalService.CloseManager(commandContext.Guild.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify GlobalMusicService during cleanup.");
        }

        // Dispose local resources
        DisposeInternal();
        logger.LogInformation("Playback handler cleanup complete.");
    }
    
    // Dispose pattern implementation
    private void DisposeInternal()
    {
        try
        {
            _playbackTask?.Dispose();
            _runnerCts?.Dispose();
            _skipSongCts?.Dispose();
            _inactivityCts?.Dispose();
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogDebug(ex, "Some resources were already disposed during DisposeInternal.");
        }
    }
    
    // Primary playback loop
    private async Task PlaybackRunnerAsync(VoiceClient voiceClient)
    {
        try
        {
            logger.LogInformation("Playback runner has started!");
            await voiceClient.StartAsync();
            await voiceClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);

            await using var outStream = voiceClient.CreateOutputStream();
            await using var opusEncodeStream =
                new OpusEncodeStream(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

            _runnerCts = new CancellationTokenSource();
            var runnerToken = _runnerCts.Token;

            while (!runnerToken.IsCancellationRequested)
            {
                _inactivityCts?.Dispose();
                _inactivityCts = new CancellationTokenSource();
                try
                {
                    while (!queueManager.IsEmpty() && !runnerToken.IsCancellationRequested)
                    {
                        await PlaySongAsync(opusEncodeStream);
                        if (runnerToken.IsCancellationRequested) break;
                    }

                    if (runnerToken.IsCancellationRequested)
                    {
                        logger.LogInformation("Playback stopped by request.");
                        break;
                    }

                    logger.LogInformation("Queue is empty. Starting inactivity timer.");
                    await TrySendMessageAsync("Reached the end of the queue.");

                    // Wait for inactivity or cancellation
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(10), _inactivityCts.Token);
                        logger.LogInformation("Inactivity timer completed. Stopping playback.");
                        break;
                    }
                    catch (TaskCanceledException)
                    {
                        logger.LogDebug("Inactivity timer was cancelled, continuing playback.");
                    }
                }
                catch (ObjectDisposedException)
                {
                    logger.LogError("Inactivity timer was disposed. Stopping playback.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal exception in the playback runner. {Exception}", ex);
            await TrySendMessageAsync("Fatal error occurred during playback.");
        }
        finally
        {
            logger.LogInformation("Playback runner has stopped.");
            await LeaveVoiceAsync();
        }
    }
    
    // Play a single song from the queue
    private async Task PlaySongAsync(OpusEncodeStream outStream)
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
            if (songStream == null)
            {
                logger.LogWarning("No stream could be resolved for the current song.");
                await TrySendMessageAsync("Could not resolve a playable stream for this song.");
                return;
            }
            await audioService.StartAudioStreamAsync(songStream, outStream, _skipSongCts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Skipping song due to cancellation request.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP request failed while playing song.");
            await TrySendMessageAsync("An HTTP error occurred while trying to play the song.");
        }
        catch (InvalidAudioException ex)
        {
            logger.LogError(ex, "Unsupported audio format encountered while playing song.");
            await TrySendMessageAsync($"The audio format ``{ex.AudioFormat}`` is not supported.");
        }
        catch (ApplicationException ex)
        {
            logger.LogError(ex, "Application error occurred while playing song.");
            await TrySendMessageAsync($"An error occurred during decoding:\n```{ex.Message}```");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unknown error occurred while trying to play the song. {Exception}", ex);
            await TrySendMessageAsync("An unknown error occurred while trying to play the song.");
        }
        finally
        {
            // Remove the song from the queue after playback or error
            queueManager.RemoveCurrent();
        }
    }

    // Helper to send messages to the invocation context
    private async Task TrySendMessageAsync(string message)
    {
        try
        {
            await commandContext.Channel.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send invocation message.");
        }
    }
}