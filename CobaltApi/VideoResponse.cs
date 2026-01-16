namespace CobaltApi;

public readonly struct VideoResponse
{
    public string Type { get; init; }
    public string Service { get; init; }
    public string[] Tunnel { get; init; }
    public string Filename { get; init; }
    public string Title { get; init; }
    public string Artist { get; init; }
    public string Album { get; init; }
    public string Copyright { get; init; }
    public string TypeOfFile { get; init; }
    public string AudioFormat { get; init; }
    public string Bitrate { get; init; }

    public override string ToString()
    {
        var details =
            $"Type: {Type}\n" +
            $"Service: {Service}\n" +
            $"Tunnel: {string.Join(", ", Tunnel)}\n" +
            $"Filename: {Filename}\n" +
            $"Title: {Title}\n" +
            $"Artist: {Artist}\n" +
            $"Album: {Album}\n" +
            $"Copyright: {Copyright}\n" +
            $"Type of File: {TypeOfFile}\n" +
            $"Audio Format: {AudioFormat}\n" +
            $"Bitrate: {Bitrate}";
        return details;
    }
}