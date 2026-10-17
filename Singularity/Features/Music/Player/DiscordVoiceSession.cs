using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Services.ApplicationCommands;
using Singularity.Features.Music.Services;
using Singularity.Observability;

namespace Singularity.Features.Music.Player;

/// <summary>
///     One guild's Discord voice connection: joins the channel, sends Opus audio, reports who is listening, leaves.
///     Registered per guild (scoped), so each server has its own connection and its own <see cref="AudioService" />.
/// </summary>
internal sealed class DiscordVoiceSession(AudioService audio, BotMetrics metrics, ILogger<DiscordVoiceSession> logger)
    : IVoiceSession, ITrackPlayer, IAsyncDisposable
{
    private readonly SemaphoreSlim _joinLock = new(1, 1);
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private GatewayClient? _client;
    private ulong _guildId;
    private VoiceClient? _voice;
    private Stream? _voiceStream;
    private OpusEncodeStream? _opus;
    private int _left;

    public event Action<bool>? Disconnected;

    internal bool IsJoined => _voice is not null;

    public TimeSpan Position => audio.Position;

    /// <summary>
    ///     Joins the invoking user's voice channel unless already connected. Concurrent callers share one join.
    ///     Returns true if this call made the connection.
    /// </summary>
    internal async Task<bool> JoinAsync(ApplicationCommandContext context)
    {
        await _joinLock.WaitAsync();
        try
        {
            if (_voice is not null) return false;

            var guild = context.Guild ?? throw new VoiceJoinException("This command can only be used in a server.");
            var voiceState = guild.VoiceStates.GetValueOrDefault(context.User.Id);
            if (voiceState is not { ChannelId: { } channelId })
                throw new VoiceJoinException("Join a voice channel first, then try again.");

            _client = context.Client;
            _guildId = guild.Id;

            VoiceClient voice;
            try
            {
                voice = await context.Client.JoinVoiceChannelAsync(_guildId, channelId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Missing permissions, a full channel, Discord being down...: say so, keep the cause for the log.
                throw new VoiceJoinException(
                    "I couldn't join your voice channel. Check that I can connect and speak there, then try again.", ex);
            }

            voice.Disconnect += args =>
            {
                Disconnected?.Invoke(args.Reconnect);
                return ValueTask.CompletedTask;
            };

            _voice = voice;
            metrics.SessionOpened();
            logger.LogInformation("Joined voice channel.");
            return true;
        }
        finally
        {
            _joinLock.Release();
        }
    }

    public async Task PlayAsync(Stream input, CancellationToken cancellationToken)
    {
        var output = await EnsureStartedAsync();
        await audio.StartAudioStreamAsync(input, output, cancellationToken);
    }

    // Opens the audio pipe on first use; it is then reused for every track.
    private async Task<Stream> EnsureStartedAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            if (_opus is not null) return _opus;

            var voice = _voice ?? throw new VoiceJoinException("I'm not connected to a voice channel.");

            try
            {
                await voice.StartAsync();
                await voice.EnterSpeakingStateAsync(new SpeakingProperties(SpeakingFlags.Microphone));

                _voiceStream = voice.CreateVoiceStream();
                _opus = new OpusEncodeStream(_voiceStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);
                return _opus;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new VoiceJoinException(
                    "I joined the channel but couldn't start sending audio. Try /leave and then /play again.", ex);
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    public int? ListenerCount()
    {
        if (_client is null || !_client.Cache.Guilds.TryGetValue(_guildId, out var guild)) return null;
        if (!guild.VoiceStates.TryGetValue(_client.Id, out var botState) || botState.ChannelId is not { } channelId)
            return null;

        return guild.VoiceStates.Count(kv => kv.Key != _client.Id && kv.Value.ChannelId == channelId);
    }

    public async Task LeaveAsync()
    {
        if (_client is null || _voice is null) return;
        if (Interlocked.Exchange(ref _left, 1) != 0) return;

        try
        {
            await _client.UpdateVoiceStateAsync(new VoiceStateProperties(_guildId, null));
            logger.LogDebug("Left voice channel.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to leave voice channel.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_opus is not null) await _opus.DisposeAsync();
            if (_voiceStream is not null) await _voiceStream.DisposeAsync();
            _voice?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error while disposing the voice session.");
        }

        _joinLock.Dispose();
        _startLock.Dispose();
    }
}
