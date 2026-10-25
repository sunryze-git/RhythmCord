using NetCord;
using NetCord.Rest;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Player;
using Singularity.Infrastructure;

namespace Singularity.Features.Music.Views;

/// <summary>Every Components V2 message the music feature sends.</summary>
internal static class MusicViews
{
    // Discord's limits: 100 characters per menu label, description and value, 25 options per menu.
    private const int MenuTextLimit = 100;
    private const int MaxMenuOptions = 25;

    private static readonly Color Blue = new(0, 122, 255);
    private static readonly Color Yellow = new(255, 204, 0);
    private static readonly Color Red = new(255, 59, 48);

    internal static MessageView Notice(string text, bool failure = false) =>
        Card(failure ? Red : Blue, new TextDisplayProperties(Escape(text)));

    internal static MessageView Added(IReadOnlyList<MusicTrackNew> songs, bool playsNext)
    {
        var song = songs[0];
        var title = songs.Count > 1 ? "Added playlist to queue" : playsNext ? "Playing next" : "Added to queue";
        var details = new TextDisplayProperties($"**{Link(song)}**\n*{Escape(song.Author)}*\n{Length(song)}");

        List<IComponentContainerComponentProperties> parts =
        [
            new TextDisplayProperties($"### {title}"),
            WithThumbnail(song, details)
        ];

        if (songs.Count > 1)
            parts.Add(new TextDisplayProperties($"-# …and {songs.Count - 1} more"));

        return new MessageView([new ComponentContainerProperties(parts) { AccentColor = Blue }]);
    }

    internal static MessageView NowPlaying(PlayerSnapshot snapshot, TimeSpan position)
    {
        if (snapshot.Current is not { } song)
            return Notice("Nothing is playing right now. Use /play to start something.", failure: true);

        var progress = song.Duration is { } length ? $"{position.ToClockString()} / {length.ToClockString()}" : "Live stream";
        var votes = snapshot.SkipVotes > 0 ? $" · {snapshot.SkipVotes} skip vote(s)" : string.Empty;

        var details = new TextDisplayProperties(
            $"**{Link(song)}**\n*{Escape(song.Author)}*\n{progress} · queued by {Mention(song.RequestedBy)}");

        return new MessageView(
        [
            new ComponentContainerProperties(
            [
                new TextDisplayProperties("### Now playing"),
                WithThumbnail(song, details),
                new ComponentSeparatorProperties(),
                new TextDisplayProperties($"-# Loop: {snapshot.Loop} · {snapshot.Pending.Count} up next{votes}"),
                new ActionRowProperties(
                [
                    new ButtonProperties(MusicIds.Skip, "Skip", EmojiProperties.Standard("⏭️"), ButtonStyle.Secondary),
                    LoopButton(snapshot.Loop),
                    new ButtonProperties(MusicIds.Shuffle, "Shuffle", EmojiProperties.Standard("🔀"), ButtonStyle.Secondary),
                    new ButtonProperties(MusicIds.Stop, "Stop", EmojiProperties.Standard("⏹️"), ButtonStyle.Danger)
                ]),
                new ActionRowProperties(
                [
                    new ButtonProperties(MusicIds.Queue, "Queue", EmojiProperties.Standard("📜"), ButtonStyle.Secondary),
                    new ButtonProperties(MusicIds.Refresh, "Refresh", EmojiProperties.Standard("🔄"), ButtonStyle.Secondary)
                ])
            ]) { AccentColor = Yellow }
        ]);
    }

    internal static MessageView Queue(PlayerSnapshot snapshot, int page, int pageSize)
    {
        var playing = snapshot.Current is { } current ? $"Now playing: **{Link(current)}**" : "Nothing is playing.";
        var songs = snapshot.Pending;

        if (songs.IsEmpty)
            return Card(Red, new TextDisplayProperties($"### Queue is empty\n{playing}\nUse /play to add songs."));

        pageSize = Math.Clamp(pageSize, 1, MaxMenuOptions);
        var pageCount = (songs.Count + pageSize - 1) / pageSize;
        page = Math.Clamp(page, 1, pageCount);
        var offset = (page - 1) * pageSize;
        var shown = songs.Skip(offset).Take(pageSize).ToList();

        var lines = shown.Select((song, index) =>
            $"`{offset + index + 1}` **{Escape(Truncate(song.Title, 70))}** · {Escape(Truncate(song.Author, 30))} · {Length(song)} · {Mention(song.RequestedBy)}");

        return new MessageView(
        [
            new ComponentContainerProperties(
            [
                new TextDisplayProperties($"### Up next\n{playing}"),
                new ComponentSeparatorProperties(),
                new TextDisplayProperties(string.Join('\n', lines)),
                new ComponentSeparatorProperties(),
                new TextDisplayProperties($"-# {songs.Count} queued · page {page}/{pageCount} · Loop: {snapshot.Loop}"),
                RemoveMenu(shown, offset, page),
                new ActionRowProperties(
                [
                    new ButtonProperties(MusicIds.PageOf(page - 1), "Previous", EmojiProperties.Standard("◀️"), ButtonStyle.Secondary) { Disabled = page <= 1 },
                    new ButtonProperties(MusicIds.PageOf(page), "Refresh", EmojiProperties.Standard("🔄"), ButtonStyle.Secondary),
                    new ButtonProperties(MusicIds.PageOf(page + 1), "Next", EmojiProperties.Standard("▶️"), ButtonStyle.Secondary) { Disabled = page >= pageCount }
                ])
            ]) { AccentColor = Blue }
        ]);
    }

    internal static MessageView SearchResults(string query, string token, IReadOnlyList<MusicTrackNew> results)
    {
        var lines = results.Select((song, index) =>
            $"`{index + 1}` **{Escape(Truncate(song.Title, 80))}** · {Escape(Truncate(song.Author, 30))} · {Length(song)}");

        var menu = new StringMenuProperties(
            MusicIds.SearchFor(token),
            results.Take(MaxMenuOptions).Select((song, index) =>
                new StringMenuSelectOptionProperties($"{index + 1}. {Truncate(song.Title, MenuTextLimit - 4)}", index.ToString())
                {
                    Description = Truncate(song.Author, MenuTextLimit)
                }))
        {
            Placeholder = "Pick a song to queue"
        };

        return new MessageView(
        [
            new ComponentContainerProperties(
            [
                new TextDisplayProperties($"### Results for “{Escape(Truncate(query, 100))}”"),
                new TextDisplayProperties(string.Join('\n', lines)),
                menu
            ]) { AccentColor = Blue }
        ]);
    }

    private static MessageView Card(Color accent, TextDisplayProperties text) =>
        new([new ComponentContainerProperties([text]) { AccentColor = accent }]);

    // Section text and a thumbnail side by side; just the text when the track has no picture.
    private static IComponentContainerComponentProperties WithThumbnail(MusicTrackNew song, TextDisplayProperties text) =>
        song.ThumbnailUrl is { } url
            ? new ComponentSectionProperties(new ComponentSectionThumbnailProperties(new ComponentMediaProperties(url)), [text])
            : text;

    private static ButtonProperties LoopButton(LoopMode mode) => mode switch
    {
        LoopMode.Track => new ButtonProperties(MusicIds.Loop, "Loop: Track", EmojiProperties.Standard("🔂"), ButtonStyle.Primary),
        LoopMode.Queue => new ButtonProperties(MusicIds.Loop, "Loop: Queue", EmojiProperties.Standard("🔁"), ButtonStyle.Primary),
        _ => new ButtonProperties(MusicIds.Loop, "Loop: Off", EmojiProperties.Standard("🔁"), ButtonStyle.Secondary)
    };

    private static StringMenuProperties RemoveMenu(IReadOnlyList<MusicTrackNew> shown, int offset, int page) =>
        new(MusicIds.RemoveFrom(page),
            shown.Select((song, index) =>
                new StringMenuSelectOptionProperties($"{offset + index + 1}. {Truncate(song.Title, MenuTextLimit - 6)}", song.Id)
                {
                    Description = Truncate(song.Author, MenuTextLimit)
                }))
        {
            Placeholder = "Remove songs from the queue",
            MinValues = 1,
            MaxValues = shown.Count
        };

    private static string Link(MusicTrackNew song) =>
        $"[{Escape(Truncate(song.Title, 100))}]({song.Url.Replace(")", "%29")})";

    private static string Length(MusicTrackNew song) => song.Duration?.ToClockString() ?? "Live";

    private static string Mention(ulong userId) => userId == 0 ? "someone" : $"<@{userId}>";

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    // Track titles and search words are user-controlled text; keep them from turning into formatting.
    private static string Escape(string text)
    {
        var escaped = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '~' or '`' or '|' or '>' or '[' or ']' or '#') escaped.Append('\\');
            escaped.Append(c);
        }

        return escaped.ToString();
    }
}
