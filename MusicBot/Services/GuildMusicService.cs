using Microsoft.Extensions.Logging;
using MusicBot.Utilities;
using NetCord;
using NetCord.Gateway.Voice;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using YoutubeExplode.Videos;

namespace MusicBot.Services;

public class GuildMusicService(
    ILogger<GuildMusicService> logger,
    AudioService audioService,
    YoutubeService youtubeService) : IDisposable
{
    // Context-specific dependencies
    private TextChannel? _invokedChannel;
    private Task? _playbackTask;
    private CancellationTokenSource? _stopRunnerCts;
    private CancellationTokenSource? _skipSongCts;
    private CancellationTokenSource? _inactivityCts;
    
    // Audio Client I hate it
    private VoiceClient? _voiceClient;

    public event Func<ulong, Task>? PlaybackFinished;
    
    public List<IVideo> SongQueue { get; private set; } = [];
    public IVideo? CurrentSong { get; private set; }
    public bool Active => _playbackTask is
        { Status: TaskStatus.Running or TaskStatus.WaitingForActivation or TaskStatus.WaitingToRun };

    internal void SetContextDependencies(TextChannel channel)
    {
        _invokedChannel = channel;
    }

    // Dispose is called after the bot has left VC.
    // It handles closing everything out before the GlobalManager deletes this.
    public void Dispose()
    {
        LogInfo("Disposing guild music service.");
        
        // It is expected that when Dispose is called, the bot is not in a voice channel.
        // Dispose is only called when PlaybackRunner is finished.

        if (_playbackTask!.Status == TaskStatus.Running)
        {
            throw new InvalidOperationException("Playback task is still running.");
        }
        
        // Ensure the queue is empty
        SongQueue.Clear();
        
        // Cancel any remaining tasks
        _inactivityCts?.Dispose();
        _inactivityCts = null;
        _skipSongCts?.Dispose();
        _skipSongCts = null;
        _stopRunnerCts?.Dispose();
        _stopRunnerCts = null;
        _voiceClient?.Dispose();
        _voiceClient = null;
        
        // Clear the current song
        CurrentSong = null;
        
        GC.SuppressFinalize(this);
        LogInfo("Guild music service disposed and ready to be destroyed.");
    }
    
    private void StartQueue(ApplicationCommandContext context, VoiceClient voiceClient)
    {
        LogInfo("Beginning playback of queue.");
        
        // new song, so cancel any previous inactivity timeout.
        _inactivityCts?.Cancel();
        
        // If there is no playback task, start one.
        if (_playbackTask == null)
        {
            _playbackTask = Task.Run(() => PlaybackRunner(context, voiceClient));
            LogInfo("Playback task started.");
        }
        else
        {
            LogInfo("Playback task already running.");
        }
    }

    public void StopQueue()
    {
        LogInfo("Stopping playback of queue.");
        
        // Clearing the queue, and then initiating a skip, will latch the runner into inactivity timeout.
        SongQueue.Clear();
        _skipSongCts?.Cancel();
    }

    public void SkipSong()
    {
        LogInfo("Calling for a song skip token.");
        _skipSongCts?.Cancel();
    }

    public bool LoopSong() => audioService.Looping = !audioService.Looping;

    public void Shuffle()
    {
        if (SongQueue.Count <= 1) return;
        
        LogInfo("Shuffling the queue.");
        SongQueue.Shuffle();
    }
    
    public async Task<IVideo?> AddToQueueAsync(string term, bool next, ApplicationCommandContext context, VoiceClient? voiceClient)
    {
        // Let the caller know we are attempting to do this.
        var callback = InteractionCallback.DeferredMessage(MessageFlags.Loading);
        await context.Interaction.SendResponseAsync(callback);

        // Update our internal voiceClient, since this will represent a "new" VC.
        if (voiceClient != null)
        {
            _voiceClient = voiceClient;
        }
        
        var songsToAdd = await GetSongsFromTermAsync(term);
        if (!songsToAdd.Any())
        {
            LogInfo("There were no results from the given term.");
            await context.Interaction.ModifyResponseAsync(message => message.Content = "No results from the given term.");
            return null;
        }

        var added = AddSongsToQueue(songsToAdd, next);
        await context.Interaction.ModifyResponseAsync(message => message.Content = $"Added {added} songs to queue.");
        
        StartQueue(context, _voiceClient!);
        
        return songsToAdd[0];
    }

    private int AddSongsToQueue(IReadOnlyList<IVideo> songs, bool next)
    {
        var addedCount = 0;
        foreach (var song in songs)
        {
            if (next)
            {
                SongQueue.Insert(0, song);
            }
            else
            {
                SongQueue.Add(song);
            }
            addedCount++;
        }

        return addedCount;
    }

    private async Task PlaybackRunner(ApplicationCommandContext context, VoiceClient audioClient)
    {
        try
        {
            LogInfo("Playback runner has started.");
            await audioClient.StartAsync();
            await audioClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);
            await using var outStream = audioClient.CreateOutputStream();
            await using OpusEncodeStream opusEncodeStream =
                new(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);
            
            _stopRunnerCts = new CancellationTokenSource();
            // This is the main playback loop.
            // The only time this should ever exit is upon inactivity, an error, or upon request.
            while (!_stopRunnerCts.IsCancellationRequested)
            {
                // Reset the inactivity token upon new iteration.
                _inactivityCts?.Dispose();
                _inactivityCts = new CancellationTokenSource();
                try
                {
                    // Queue loop
                    while (SongQueue.Count > 0)
                    {
                        await PlayNextSong(opusEncodeStream);

                        // Check between songs if we need to stop.
                        if (_stopRunnerCts.IsCancellationRequested)
                        {
                            break;
                        }
                    }

                    LogInfo("Reached the end of the queue.");
                    await context.Channel.SendMessageAsync("Reached the end of the queue.");

                    // Queue Loop is now empty. Sit here for 10 minutes.
                    // If a new song is added this task will throw a TaskCanceledException.
                    // That will be caught in the catch block and we will continue.
                    await Task.Delay(TimeSpan.FromMinutes(10), _inactivityCts.Token);

                    // If we are here, inactivity timeout was reached.
                    // Call the stop runner token, which will cause the playback task to exit.
                    LogInfo("Inactivity timeout reached. Stopping playback runner.");
                    break;
                }
                catch (TaskCanceledException)
                {
                    // Inactivity token was cancelled, but we probably got a new song.
                    // This is expected behavior.
                    LogInfo("Inactivity timeout was cancelled.");
                }
                catch (ObjectDisposedException)
                {
                    // Inactivity token was disposed, meaning we probably are being Disposed.
                    LogInfo("Inactivity timeout was disposed of. Something bad happened, so we should leave.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogError(ex, "Fatal exception in playback runner.");
            try
            {
                await context.Channel.SendMessageAsync("⚠️ A fatal error occurred during playback.");
            }
            catch
            {
                // Ignore
            }
        }
        finally
        {
            // The only reason we should now be here is upon request to leave.
            LogInfo("Playback runner has stopped.");
                    
            // Trigger the event.
            LogInfo($"Invoking PlaybackFinished for guild {context.Guild!.Id}.");
            if (PlaybackFinished != null)
            {
                await PlaybackFinished.Invoke(context.Guild!.Id);
            }
            LogInfo("PlaybackRunner has finished execution.");
        }
    }

    private async Task PlayNextSong(OpusEncodeStream outStream)
    {
        Stream? inputStream = null;
        _skipSongCts = new CancellationTokenSource();
        
        try
        {
            var songIteration = SongQueue.First();
            CurrentSong = songIteration; // update public property
            SongQueue.RemoveAt(0);

            LogInfo($"Playing song '{CurrentSong?.Title ?? "Unknown"}'");
            
            inputStream = songIteration is CustomSong custom
                ? custom.Source
                : await youtubeService.GetAudioStreamAsync(songIteration);
                        
            if (inputStream == null) throw new InvalidOperationException("Input stream is null.");

            await Task.Delay(50, _skipSongCts.Token);
            await audioService.StartAudioStream(inputStream, outStream, _skipSongCts.Token);
        }
        catch (OperationCanceledException)
        {
            LogWarning("Current song playback was cancelled.");
        }
        catch (HttpRequestException e)
        {
            LogError(e, $"HTTP request failure on '{CurrentSong?.Title ?? "Unknown"}' - continuing to next song.");
            await SendErrorMessage($"⚠️ HTTP Error when playing '{CurrentSong?.Title ?? "Unknown"}: {e.StatusCode}'." +
                                  $"YouTubeExplode may need to be updated, or you are using the bot too fast.");
        }
        catch (Exception ex)
        {
            LogError(ex, $"Failed to play song '{CurrentSong?.Title ?? "Unknown"}' - continuing to next song.");
            await SendErrorMessage($"⚠️ Error playing '{CurrentSong?.Title ?? "Unknown"}'.");
        }
        finally
        {
            // Dispose of input stream if there is one
            if (inputStream != null && CurrentSong is not CustomSong)
            {
                await inputStream.DisposeAsync();
            }
            
            // Reset current song if queue is empty
            if (SongQueue.Count == 0)
            {
                CurrentSong = null;
            }
        }
    }

    private async Task SendErrorMessage(string message)
    {
        await _invokedChannel!.SendMessageAsync(message);
    }

    private static bool IsLikelyAudioFile(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        try
        {
            var uri = new Uri(url);
            var extension = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
            string[] audioExtensions = [".wav", ".mp3", ".ogg", ".flac", ".m4a", ".wma", ".opus"];
            return Array.Exists(audioExtensions, x => x == extension);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<IVideo>> GetSongsFromTermAsync(string term)
    {
        if (term.StartsWith("https://"))
        {
            if (term.Contains("youtube.com/playlist"))
            {
                LogInfo("Adding playlist to queue by URL.");
                return await youtubeService.GetPlaylistVideosAsync(term);
            }
            
            if (term.Contains("youtube.com/watch"))
            {
                LogInfo("Adding a song to queue by URL.");
                var video = await youtubeService.GetVideoAsync(term);
                if (video is null) return [];
                return [video];
            }

            if (IsLikelyAudioFile(term))
            {
                LogInfo("Adding local file to queue by URL.");
                var uri = new Uri(term);
                var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                return [new CustomSong(term, name, await SearchService.GetStreamFromUri(uri))];
            }
        }
        
        LogInfo("Adding a song to queue by search.");
        var videoObject = await youtubeService.GetVideoAsync(term);
        return videoObject != null ? [videoObject] : [];
    }
    
    internal async Task LeaveVoiceAsync()
    {
        // This is called when we need to leave VC for any reason.
        LogInfo("Leaving voice channel upon request.");
        
        // Also cancel the playback task.
        await _stopRunnerCts!.CancelAsync();
        await _skipSongCts!.CancelAsync();
        await _inactivityCts!.CancelAsync();
    }

    // Simplified logging methods
    private void LogInfo(string message) => 
        logger.LogInformation("{Message}", message);
    
    private void LogWarning(string message) => 
        logger.LogWarning("{Message}", message);
    
    private void LogError(Exception ex, string message) => 
        logger.LogError(ex, "{Message}",  message);
}
