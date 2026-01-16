namespace CobaltApi;

public class Request(
    string url,
    string audioBitrate = "128",
    string audioFormat = "mp3",
    string downloadMode = "audio",
    string filenameStyle = "basic",
    string videoQuality = "144",
    bool disableMetadata = false,
    bool alwaysProxy = false,
    bool localProcessing = true,
    bool youtubeBetterAudio = true
    )
{
    public string Url { get; } = url ?? throw new ArgumentNullException(nameof(url));
    public string AudioBitrate { get; } = audioBitrate;
    public string AudioFormat { get; } = audioFormat;
    public string DownloadMode { get; } = downloadMode;
    public string FilenameStyle { get; } = filenameStyle;
    public string VideoQuality { get; } = videoQuality;
    public bool DisableMetadata { get; } = disableMetadata;
    public bool AlwaysProxy { get; } = alwaysProxy;
    public bool LocalProcessing { get; } = localProcessing;
    public bool YoutubeBetterAudio { get; } = youtubeBetterAudio;
    
    public override string ToString() =>
        $"""
         Request to URL: {Url}
                 Audio Bitrate: {AudioBitrate}
                 Audio Format: {AudioFormat}
                 Download Mode: {DownloadMode}
                 Filename Style: {FilenameStyle}
                 Video Quality: {VideoQuality}
                 Disable Metadata: {DisableMetadata}
                 Always Proxy: {AlwaysProxy}
                 Local Processing: {LocalProcessing}
                 YouTube Better Audio: {YoutubeBetterAudio}
         """;
}