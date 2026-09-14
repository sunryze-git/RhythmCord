using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Services.ApplicationCommands;

namespace Singularity.Features.Music.Services;

internal class PlaybackHandler(
    ILogger<PlaybackHandler> logger,
    AudioService audioService,
    QueueManager queueManager,
    MediaResolver mediaResolver,
    GuildAudioInstanceOrchestrator orchestrator) : IAsyncDisposable
{
    private ApplicationCommandContext _commandContext = null!;
    private CancellationTokenSource? _inactivityCts;

    private Task? _playbackTask;
    private CancellationTokenSource? _runnerCts;
    private CancellationTokenSource? _skipSongCts;
    private VoiceClient? _voiceClient;
    private int _isShuttingDown;

    internal bool Active =>
        _playbackTask?.Status is TaskStatus.Running or TaskStatus.WaitingForActivation or TaskStatus.WaitingToRun;

    internal bool Initialized => _voiceClient != null;

    internal ImmutableList<MusicTrackNew> SongQueue => queueManager.SongQueue;
    internal MusicTrackNew? CurrentSong => queueManager.CurrentSong;
    internal TimeSpan Duration => CurrentSong?.Duration ?? TimeSpan.Zero;
    internal TimeSpan Position => audioService.Position;
    internal void SkipSong() => _skipSongCts?.Cancel();
    internal void SetContext(ApplicationCommandContext context) => _commandContext = context;

    internal bool ToggleLooping()
    {
        audioService.Looping = !audioService.Looping;
        return audioService.Looping;
    }

    internal void Shuffle() => queueManager.Shuffle();
    internal void Stop() => StopQueue();
    internal Task EndAsync() => LeaveVoiceAsync();

    internal async Task InitializeAsync()
    {
        _voiceClient = await JoinVoiceAsync();
        _voiceClient.Disconnect += ShutdownAsync;
    }

    private async Task<VoiceClient> JoinVoiceAsync()
    {
        var target = _commandContext.Guild!.VoiceStates.GetValueOrDefault(_commandContext.User.Id);
        if (target is not { ChannelId: not null })
            throw new InvalidOperationException("Could not determine user's voice channel.");

        return await _commandContext.Client.JoinVoiceChannelAsync(_commandContext.Guild.Id, target.ChannelId.Value);
    }

    private async Task LeaveVoiceAsync()
    {
        if (_commandContext.Guild == null || _voiceClient == null) return;
        await _commandContext.Client.UpdateVoiceStateAsync(new VoiceStateProperties(_commandContext.Guild.Id, null));
    }

    internal void StartQueue()
    {
        logger.LogInformation("Beginning playback of queue.");
        _inactivityCts?.Cancel();

        if ((_playbackTask == null || _playbackTask.IsCompleted) && _voiceClient != null)
        {
            _runnerCts?.Dispose();
            _runnerCts = new CancellationTokenSource();
            var token = _runnerCts.Token;

            _playbackTask = Task.Run(() => PlaybackRunnerAsync(_voiceClient, token));
            logger.LogInformation("Playback runner start initiated.");
        }
    }

    internal async Task<MusicTrackNew> AddSongAsync(string term, bool next)
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

    private void StopQueue()
    {
        queueManager.Clear();
        _skipSongCts?.Cancel();
    }

    private ValueTask ShutdownAsync(DisconnectEventArgs args)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
            return ValueTask.CompletedTask;

        logger.LogInformation("Shutdown requested for playback handler.");

        _voiceClient?.Disconnect -= ShutdownAsync;
        queueManager.Clear();

        if (_commandContext.Guild != null)
        {
            var guildId = _commandContext.Guild.Id;
            _ = Task.Run(async () => await orchestrator.CloseManagerAsync(guildId));
        }

        return ValueTask.CompletedTask;
    }

    private async Task PlaybackRunnerAsync(VoiceClient voiceClient, CancellationToken runnerToken)
    {
        try
        {
            logger.LogInformation("Playback runner has started!");
            await voiceClient.StartAsync();
            await voiceClient.EnterSpeakingStateAsync(new SpeakingProperties(SpeakingFlags.Microphone));

            await using var outStream = voiceClient.CreateVoiceStream();
            await using var opusEncodeStream =
                new OpusEncodeStream(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

            while (!runnerToken.IsCancellationRequested)
            {
                _inactivityCts?.Dispose();
                _inactivityCts = CancellationTokenSource.CreateLinkedTokenSource(runnerToken);

                try
                {
                    while (!queueManager.IsEmpty() && !runnerToken.IsCancellationRequested)
                    {
                        var nextTrack = queueManager.SongQueue.Skip(1).FirstOrDefault();

                        // Delegate background pre-fetching through mediaResolver
                        if (nextTrack is not null && nextTrack.PreResolvedStreamInfoTask is null)
                        {
                            mediaResolver.PreFetchStreamInfo(nextTrack);
                            logger.LogDebug("Initiated stream info pre-fetch for next track: {TrackTitle}", nextTrack.Title);
                        }

                        await PlaySongAsync(opusEncodeStream, runnerToken);
                        if (runnerToken.IsCancellationRequested) break;
                    }

                    if (runnerToken.IsCancellationRequested)
                    {
                        logger.LogInformation("Playback stopped by request.");
                        break;
                    }

                    logger.LogInformation("Queue is empty. Starting inactivity timer.");

                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(10), _inactivityCts.Token);
                        logger.LogInformation("Inactivity timer completed. Stopping playback.");
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        if (runnerToken.IsCancellationRequested)
                        {
                            logger.LogDebug("Playback runner canceled during inactivity delay.");
                            break;
                        }

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

    private async Task PlaySongAsync(OpusEncodeStream outStream, CancellationToken runnerToken)
    {
        _skipSongCts?.Dispose();
        _skipSongCts = CancellationTokenSource.CreateLinkedTokenSource(runnerToken);

        try
        {
            var next = queueManager.CurrentSong;
            if (next == null)
            {
                logger.LogWarning("No song to play, skipping.");
                return;
            }

            // MediaResolver.ResolveStreamAsync will await PreResolvedStreamInfoTask if set, or resolve on demand
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
            await queueManager.RemoveCurrentAsync();
        }
    }

    private async Task TrySendMessageAsync(string message)
    {
        try
        {
            await _commandContext.Channel.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send invocation message.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        logger.LogInformation("Disposing PlaybackHandler async resources.");
        _voiceClient?.Disconnect -= ShutdownAsync;

        if (queueManager != null)
        {
            await queueManager.ClearAsync();
        }

        try
        {
            _runnerCts?.Cancel();
            _skipSongCts?.Cancel();
            _inactivityCts?.Cancel();
        }
        catch (ObjectDisposedException) { }

        if (_playbackTask != null)
        {
            try
            {
                await _playbackTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                logger.LogWarning(ex, "Playback task took too long to cancel during disposal.");
            }
        }

        _runnerCts?.Dispose();
        _skipSongCts?.Dispose();
        _inactivityCts?.Dispose();

        GC.SuppressFinalize(this);
    }
}
