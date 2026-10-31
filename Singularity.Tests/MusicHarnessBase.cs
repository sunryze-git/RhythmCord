using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.JsonModels;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using Singularity.Features.Music;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Services;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Tests;

/// <summary>
///     A bot without Discord: NetCord's own dispatchers run on made-up interactions in a made-up guild, and everything the
///     bot sends comes back as JSON to inspect. Nothing here opens a network connection; a REST call is recorded instead.
/// </summary>
public abstract class MusicHarnessBase : IAsyncLifetime
{
    protected const ulong GuildId = 10;
    protected const ulong UserId = 20;
    protected const ulong OtherUserId = 21;
    protected const ulong BotId = 999;
    protected const ulong VoiceChannelId = 30;
    protected const ulong TextChannelId = 40;
    private const string BotToken = "OTk5.AAAAAA.BBBBBBBBBBBBBBBBBBBBBBBBBBB";

    // ---------------------------------------------------------------- fakes

    private sealed class BlockingPlayer : ITrackPlayer
    {
        public TimeSpan Position => TimeSpan.Zero;

        public async Task PlayAsync(Stream input, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Skipped or stopped: a quiet return is how the real player ends a cancelled track.
            }
        }
    }

    private sealed class StubSource : ITrackSource
    {
        public Task<IReadOnlyList<MusicTrackNew>> ResolveSongsAsync(string query) =>
            Task.FromResult<IReadOnlyList<MusicTrackNew>>(query.StartsWith("nothing") ? [] : [Tracks.Make(query)]);

        public Task<IReadOnlyList<MusicTrackNew>> SearchAsync(string query, int limit) =>
            Task.FromResult<IReadOnlyList<MusicTrackNew>>([Tracks.Make("hit0"), Tracks.Make("hit1")]);

        public Task<Stream> ResolveStreamAsync(MusicTrackNew track) => Task.FromResult<Stream>(new MemoryStream());
        public void PreFetch(MusicTrackNew track) { }
    }

    /// <summary>What went out over REST: method, path and body. Answers with an empty message.</summary>
    private sealed class RecordingRestHandler(List<RestCall> calls) : IRestRequestHandler
    {
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (calls) calls.Add(new RestCall(request.Method.Method, request.RequestUri!.AbsolutePath, body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"5","channel_id":"40","author":{"id":"999","username":"bot"},"content":"","timestamp":"2024-01-01T00:00:00+00:00","tts":false,"mention_everyone":false,"mentions":[],"mention_roles":[],"attachments":[],"embeds":[],"pinned":false,"type":0}""",
                    Encoding.UTF8, "application/json")
            };
        }

        public void AddDefaultHeader(string name, IEnumerable<string> values) { }

        public void Dispose() { }
    }

    protected sealed record RestCall(string Method, string Path, string Body);

    // ---------------------------------------------------------------- state

    protected readonly List<InteractionCallbackProperties> Responses = [];
    protected readonly List<RestCall> RestCalls = [];
    private readonly RestClient _rest;
    private readonly GatewayClient _client;
    private readonly ComponentInteractionService<ButtonInteractionContext> _buttons = new();
    private readonly ComponentInteractionService<StringMenuInteractionContext> _menus = new();
    private readonly ApplicationCommandService<ApplicationCommandContext> _commands = new();
    private ServiceProvider _services = null!;

    protected GuildAudioInstanceOrchestrator Orchestrator { get; private set; } = null!;
    protected MusicSearch Search { get; private set; } = null!;
    protected MusicScheduler Scheduler { get; private set; } = null!;
    protected Guild Guild { get; private set; } = null!;
    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    protected MusicHarnessBase()
    {
        var recording = new RestClientConfiguration { RequestHandler = new RecordingRestHandler(RestCalls) };
        _rest = new RestClient(recording);
        _client = new GatewayClient(new NetCord.BotToken(BotToken), new GatewayClientConfiguration { RestClientConfiguration = recording });
    }

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(new MusicOptions { IdleTimeout = TimeSpan.Zero, AloneCheckInterval = TimeSpan.Zero, QueuePageSize = 10, TimeZone = "UTC" });
        services.AddSingleton(new BotMetrics());
        services.AddSingleton<ErrorReporter>();
        services.AddSingleton<CommandCooldown>();
        services.AddMusicFeature();
        services.AddSingleton<ITrackSource, StubSource>();
        services.AddScoped<ITrackPlayer, BlockingPlayer>();
        _services = services.BuildServiceProvider();

        Orchestrator = _services.GetRequiredService<GuildAudioInstanceOrchestrator>();
        Search = _services.GetRequiredService<MusicSearch>();
        Scheduler = _services.GetRequiredService<MusicScheduler>();
        _buttons.AddModule<MusicButtons>();
        _menus.AddModule<MusicMenus>();
        _commands.AddModule<MusicCommands>();
        _commands.AddModule<ScheduleCommands>();
        Guild = BuildGuild(listeners: [UserId]);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Orchestrator.CloseAllManagersAsync();
        await _services.DisposeAsync();
        _client.Dispose();
    }

    // ---------------------------------------------------------------- building blocks

    // Listeners are in the voice channel with the bot. The bot can connect and speak there, as /play checks.
    protected Guild BuildGuild(ulong[] listeners, bool botConnected = true, ulong botChannelId = VoiceChannelId)
    {
        JsonVoiceState InVoice(ulong userId) =>
            new() { GuildId = GuildId, ChannelId = userId == BotId ? botChannelId : VoiceChannelId, UserId = userId };

        return new Guild(
            new JsonGuild
            {
                Id = GuildId,
                Name = "guild",
                Roles = [new JsonRole { Id = GuildId, Name = "@everyone", Permissions = Permissions.Connect | Permissions.Speak }],
                Emojis = [],
                Stickers = [],
                Channels = [new JsonChannel { Id = VoiceChannelId, Type = ChannelType.VoiceGuildChannel, GuildId = GuildId, PermissionOverwrites = [] }],
                ActiveThreads = [],
                Users = [new JsonGuildUser { User = new JsonUser { Id = BotId, Username = "bot" }, RoleIds = [] }],
                VoiceStates = [.. botConnected ? [InVoice(BotId)] : Array.Empty<JsonVoiceState>(), .. listeners.Select(InVoice)],
                Presences = [],
                StageInstances = [],
                ScheduledEvents = []
            },
            BotId,
            _rest,
            (IDictionaryProvider)Activator.CreateInstance(typeof(Guild).Assembly.GetType("NetCord.Gateway.DictionaryProvider")!)!);
    }

    private static JsonInteraction Json(InteractionType type, JsonInteractionData data, ulong userId, Permissions permissions) => new()
    {
        Id = 1,
        ApplicationId = BotId,
        Type = type,
        Token = "token",
        Entitlements = [],
        AuthorizingIntegrationOwners = new Dictionary<ApplicationIntegrationType, ulong>(),
        GuildId = GuildId,
        Channel = new JsonChannel { Id = TextChannelId, Type = ChannelType.TextGuildChannel, GuildId = GuildId },
        GuildUser = new JsonGuildUser { User = new JsonUser { Id = userId, Username = "user" }, Permissions = permissions, RoleIds = [] },
        Message = new JsonMessage
        {
            Id = 5,
            ChannelId = TextChannelId,
            Author = new JsonUser { Id = BotId, Username = "bot" },
            Content = string.Empty,
            MentionedUsers = [],
            MentionedRoleIds = [],
            MentionedChannels = [],
            Attachments = [],
            Embeds = [],
            Reactions = [],
            MessageSnapshots = [],
            Components = [],
            Stickers = []
        },
        Data = data
    };

    private Task<InteractionCallbackResponse?> Capture(
        IInteraction _, InteractionCallbackProperties callback, bool __, RestRequestProperties? ___, CancellationToken ____)
    {
        Responses.Add(callback);
        return Task.FromResult<InteractionCallbackResponse?>(null);
    }

    protected async Task<IExecutionResult> PressAsync(string customId, ulong userId = UserId, Permissions permissions = default, Guild? guild = null)
    {
        var data = new JsonInteractionData { CustomId = customId, ComponentType = ComponentType.Button };
        var interaction = new ButtonInteraction(Json(InteractionType.MessageComponent, data, userId, permissions), guild ?? Guild, Capture, _rest);
        return await _buttons.ExecuteAsync(new ButtonInteractionContext(interaction, _client), _services);
    }

    protected async Task<IExecutionResult> ChooseAsync(
        string customId, string[] values, ulong userId = UserId, Permissions permissions = default, Guild? guild = null)
    {
        var data = new JsonInteractionData { CustomId = customId, ComponentType = ComponentType.StringMenu, SelectedValues = values };
        var interaction = new StringMenuInteraction(Json(InteractionType.MessageComponent, data, userId, permissions), guild ?? Guild, Capture, _rest);
        return await _menus.ExecuteAsync(new StringMenuInteractionContext(interaction, _client), _services);
    }

    /// <summary>Runs a slash command. Options are (name, type, value).</summary>
    protected async Task<IExecutionResult> RunCommandAsync(
        string name,
        (string Name, ApplicationCommandOptionType Type, string Value)[]? options = null,
        ulong userId = UserId,
        Permissions permissions = default,
        Guild? guild = null) =>
        await RunCommandAsync(name, Options(options), userId, permissions, guild);

    /// <summary>Runs a subcommand, such as /schedule play. Options are (name, type, value).</summary>
    protected async Task<IExecutionResult> RunSubcommandAsync(
        string name,
        string subcommand,
        (string Name, ApplicationCommandOptionType Type, string Value)[]? options = null,
        ulong userId = UserId,
        Permissions permissions = default,
        Guild? guild = null)
    {
        var sub = new JsonApplicationCommandInteractionDataOption
        {
            Name = subcommand,
            Type = ApplicationCommandOptionType.SubCommand,
            Options = Options(options)
        };
        return await RunCommandAsync(name, [sub], userId, permissions, guild);
    }

    private async Task<IExecutionResult> RunCommandAsync(
        string name, JsonApplicationCommandInteractionDataOption[] options, ulong userId, Permissions permissions, Guild? guild)
    {
        var data = new JsonInteractionData { Id = 1, Name = name, Type = ApplicationCommandType.ChatInput, Options = options };
        var interaction = new SlashCommandInteraction(Json(InteractionType.ApplicationCommand, data, userId, permissions), guild ?? Guild, Capture, _rest);
        return await _commands.ExecuteAsync(new ApplicationCommandContext(interaction, _client), _services);
    }

    private static JsonApplicationCommandInteractionDataOption[] Options((string Name, ApplicationCommandOptionType Type, string Value)[]? options) =>
        (options ?? []).Select(o => new JsonApplicationCommandInteractionDataOption { Name = o.Name, Type = o.Type, Value = o.Value }).ToArray();

    /// <summary>Makes the guild what the gateway reports, as background work such as a scheduled play reads it.</summary>
    protected void CacheGuild(Guild guild)
    {
        // GatewayClient.Cache has no public setter.
        var cache = typeof(GatewayClient).GetField("<Cache>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
        cache.SetValue(_client, _client.Cache.CacheGuild(guild));
    }

    /// <summary>Messages the bot posted in the text channel by itself, as JSON.</summary>
    protected List<JsonNode> ChannelMessages()
    {
        lock (RestCalls)
            return RestCalls.Where(c => c.Method == "POST" && c.Path.EndsWith($"/channels/{TextChannelId}/messages"))
                .Select(c => JsonNode.Parse(c.Body)!).ToList();
    }

    // A running session with a track playing and the rest of the titles waiting in the queue.
    private protected async Task<GuildPlayer> PlayAsync(params string[] titles)
    {
        var player = Orchestrator.GetOrCreateManager(GuildId).Player;
        await player.EnqueueAsync(titles.Select(t => Tracks.Make(t)).ToList(), next: false, requestedBy: UserId);
        await WaitUntil(() => player.Snapshot.Current is not null, "the first track to start");
        return player;
    }

    protected static async Task WaitUntil(Func<bool> condition, string because)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Timed out waiting for: " + because);
    }

    protected async Task<JsonObject> ResponseAsync()
    {
        var callback = Assert.Single(Responses);
        return (JsonObject)JsonNode.Parse(await callback.Serialize().ReadAsStringAsync())!;
    }

    protected static string TextIn(JsonNode? node) => node switch
    {
        JsonObject o => (o["type"]?.GetValue<int>() == 10 ? (string)o["content"]! : string.Empty) + string.Concat(o.Select(p => TextIn(p.Value))),
        JsonArray a => string.Concat(a.Select(TextIn)),
        _ => string.Empty
    };

    protected static void AssertSucceeded(IExecutionResult result) =>
        Assert.True(result is not IFailResult, (result as IFailResult)?.Message ?? (result as IExceptionResult)?.Exception.ToString());

    /// <summary>The slash commands exactly as they would be registered with Discord.</summary>
    protected async Task<JsonArray> RegisteredCommandsAsync()
    {
        try
        {
            await _commands.RegisterCommandsAsync(_rest, BotId, guildId: null);
        }
        catch (Exception)
        {
            // The empty answer is not a list of commands, which does not matter: only the request is of interest.
        }

        return JsonNode.Parse(RestCalls.Last(call => call.Method == "PUT").Body)!.AsArray();
    }

    /// <summary>The one response must be a message only its recipient sees; returns its text.</summary>
    protected async Task<string> RefusalAsync()
    {
        var json = await ResponseAsync();
        Assert.Equal(4, (int)json["type"]!);
        Assert.Equal(32768 | 64, (int)json["data"]!["flags"]!);
        return TextIn(json["data"]);
    }
}
