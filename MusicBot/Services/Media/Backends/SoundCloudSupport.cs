using System.Text.Json.Serialization;

namespace MusicBot.Services.Media.Backends;

public class SoundCloudTrack
{
    public string? Title { get; set; }
    public string? Policy { get; set; }
    public string? TrackAuthorization { get; set; }
    public SoundCloudUser? User { get; set; }
    public SoundCloudMedia? Media { get; set; }
    public int Duration { get; set; }
    
    [JsonPropertyName("artwork_url")]
    public string? ArtworkUrl { get; set; }
}

public class SoundCloudUser
{
    public string? Username { get; set; }
}

public class SoundCloudMedia
{
    public List<SoundCloudTranscoding>? Transcodings { get; set; }
}

public class SoundCloudTranscoding
{
    public string? Url { get; set; }
    public string? Preset { get; set; }
    public bool Snipped { get; set; }
    public SoundCloudFormat? Format { get; set; }
}

public class SoundCloudFormat
{
    public string? Protocol { get; set; }
}

public class SoundCloudStreamInfo
{
    public string? Url { get; set; }
}