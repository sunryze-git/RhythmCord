namespace CobaltApi;

public class Request
{
    public string url { get; set; }
    public string audioBitrate { get; set; } = "128";
    public string audioFormat { get; set; } = "mp3";
    public string downloadMode { get; set; } = "audio";
    public string filenameStyle { get; set; } = "basic";
    public string videoQuality { get; set; } = "144";
    public bool disableMetadata { get; set; } = false;
    public bool alwaysProxy { get; set; } = false;
    public bool localProcessing { get; set; } = true;
    public bool youtubeBetterAudio { get; set; } = true;
    
    public override string ToString()
    {
        return $"Request to URL: {url}\n" +
               $"Audio Bitrate: {audioBitrate}\n" +
               $"Audio Format: {audioFormat}\n" +
               $"Download Mode: {downloadMode}\n" +
               $"Filename Style: {filenameStyle}\n" +
               $"Video Quality: {videoQuality}\n" +
               $"Disable Metadata: {disableMetadata}\n" +
               $"Always Proxy: {alwaysProxy}\n" +
               $"Local Processing: {localProcessing}\n" +
               $"YouTube Better Audio: {youtubeBetterAudio}";
    }
}