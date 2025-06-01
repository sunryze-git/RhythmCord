using Newtonsoft.Json;

namespace CobaltApi.Records;

// GET Records
public record InfoResponse(
    [property: JsonProperty("cobalt")] CobaltObject Instance,
    [property: JsonProperty("git")] GitObject Git
);

public record CobaltObject(
    [property: JsonProperty("version")] string Version,
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("startTime")] string StartTime,
    [property: JsonProperty("turnstileSiteKey")] string TurnstileSiteKey,
    [property: JsonProperty("services")] string[] Services
);

public record GitObject(
    [property: JsonProperty("commit")] string Commit,
    [property: JsonProperty("branch")] string Branch,
    [property: JsonProperty("remote")] string Remote
);
    
    
// POST Response Records
// Generic Response class. Serves as a base for all responses with a 'status' key.
public record Response(
    [property: JsonProperty("status")] string Status
);

// Tunnel Redirect Response
public record TunnelRedirect(
    string Status,
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("filename")] string Filename
) : Response(Status);

// Local Processing Response
public record LocalProcessing(
    string Status,
    [property: JsonProperty("type")] string Type,
    [property: JsonProperty("service")] string Service,
    [property: JsonProperty("tunnel")] string[] Tunnel,
    [property: JsonProperty("output")] Output Output,
    [property: JsonProperty("audio")] Audio Audio,
    [property: JsonProperty("isHLS")] bool IsHls
) : Response(Status);

// Local Processing Response output object
public record Output(
    [property: JsonProperty("type")] string Type,
    [property: JsonProperty("filename")] string Filename,
    [property: JsonProperty("metadata")] OutputMetadata Metadata
);

// Local Processing Response output metadata object
public record OutputMetadata(
    [property: JsonProperty("album")] string Album,
    [property: JsonProperty("copyright")] string Copyright,
    [property: JsonProperty("title")] string Title,
    [property: JsonProperty("artist")] string Artist,
    [property: JsonProperty("track")] string Track,
    [property: JsonProperty("date")] string Date
);

// Local Processing Response audio object
public record Audio(
    [property: JsonProperty("copy")] bool Copy,
    [property: JsonProperty("format")] string Format,
    [property: JsonProperty("bitrate")] string Bitrate
);

// Picker Response
public record Picker(
    string Status,
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("thumb")] string Thumb
) : Response(Status);

// Error Response
public record ErrorResponse(
    string Status,
    [property: JsonProperty("error")] object Error
) : Response(Status);

// Error Object
public record ErrorObject(
    [property: JsonProperty("code")] string Code,
    [property: JsonProperty("context")] object Context
);

// Error Context
public record ErrorContext(
    [property: JsonProperty("service")] string Service,
    [property: JsonProperty("limit")] int Limit
);