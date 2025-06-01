using System.Net.Http.Headers;
using CobaltApi.Records;
using Newtonsoft.Json;

namespace CobaltApi;

public class CobaltClient(string instanceUrl)
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri(instanceUrl)
    };

    public async Task<InfoResponse> GetInformationAsync()
    {
        using var response = await _client.GetAsync("");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<InfoResponse>(json) ??
               throw new JsonException("Failed to deserialize InfoResponse");
    }

    public async Task<VideoResponse> GetCobaltResponseAsync(string targetUrl)
    {
        using var request = BuildRequest(new Request { url = targetUrl });
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await HandleResponseAsync(response);
    }

    public async Task<VideoResponse> GetCobaltResponseAsync(Request req)
    {
        using var request = BuildRequest(req);
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await HandleResponseAsync(response);
    }
    
    public async Task<Stream> GetTunnelStreamAsync(VideoResponse video)
    {
        var tunnelUrl = video.Tunnel.FirstOrDefault();
        if (tunnelUrl == null) throw new InvalidOperationException("No tunnel URL found in VideoResponse.");
        var tunnelResponse = await _client.GetAsync(new Uri(tunnelUrl).PathAndQuery);
        tunnelResponse.EnsureSuccessStatusCode();
        var stream = await tunnelResponse.Content.ReadAsStreamAsync();
        stream.Position = 0;
        return stream;
    }
    
    private static StringContent BuildRequestContent(Request request)
    {
        var json = JsonConvert.SerializeObject(request);
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    private static HttpRequestMessage BuildRequest(Request request)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/");
        httpRequest.Content = BuildRequestContent(request);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return httpRequest;
    }
    
    private static async Task<VideoResponse> HandleResponseAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var initialResult = JsonConvert.DeserializeObject<Response>(json);
        
        // Deserialize based on the initial status
        switch (initialResult?.Status)
        {
            case "tunnel":
            case "redirect":
            {
                var obj = JsonConvert.DeserializeObject<TunnelRedirect>(json) ??
                           throw new JsonException("Failed to deserialize TunnelRedirect");
                return new VideoResponse
                {
                    Type = "tunnel / redirect",
                    Tunnel = [obj.Url],
                    Filename = obj.Filename
                };
            }

            case "local-processing":
            {
                var obj = JsonConvert.DeserializeObject<LocalProcessing>(json) ??
                         throw new JsonException("Failed to deserialize LocalProcessing");
                return new VideoResponse
                {
                    Type = obj.Type,
                    Service = obj.Service,
                    Tunnel = obj.Tunnel,
                    Filename = obj.Output.Filename,
                    Title = obj.Output.Metadata.Title,
                    Artist = obj.Output.Metadata.Artist,
                    Album = obj.Output.Metadata.Album,
                    Copyright = obj.Output.Metadata.Copyright,
                    TypeOfFile = obj.Output.Type,
                    AudioFormat = obj.Audio.Format,
                    Bitrate = obj.Audio.Bitrate
                };
            }
                
            case "picker":
            {
                throw new NotImplementedException("Picker response handling is not implemented yet.");
            }
                
            case "error":
            {
                var obj = JsonConvert.DeserializeObject<ErrorResponse>(json) ??
                          throw new JsonException("Failed to deserialize Error");
                throw new InvalidOperationException($"Error response: {obj.Error}");
            }
                
            default:
                throw new JsonException($"Unknown response status: {initialResult?.Status}");
        }
    }
}