# Cobalt API for C#

This project is a C# wrapper for the Cobalt API. It provides a simple interface to interact with a Cobalt instance.

## Usage
Simply create an instance of the CobaltClient with your specified instance URL.
```csharp
using CobaltApi;

var client = new CobaltClient("https://api.example.com");
```

### Making requests

*Get a video response by custom request:*
```csharp
var request = new Request 
{
  url = "your video url",
  ** other parameters are available **
};
var response = await client.GetCobaltResponseAsync(request);
```
*Get a video response by URL:*
```csharp
var response = await client.GetCobaltResponseAsync("your video URL");
```

Both will return a `VideoResponse` object containing video data.

### Tunneling
The tunnel URL is how you can access the video stream. The client provides a method to get the stream:

```csharp
var tunnelUrl = response.Tunnel.FirstOrDefault();
var stream = await client.GetTunnelStreamAsync(tunnelUrl);
```

## Implemented Responses
The following responses are implemented in the Cobalt API:
- `Tunnel / Redirect` Videos are remuxed and transcoded by the server, and provided through a tunnel URL.
- `Local-Processing` Videos are not remuxed or transcoded, and are provided through a local processing URL.
- `Error` If the server encounters an error, the client will throw an exception with the error message.

The following responses are not implemented:
- `Picker` This functionality has not been implemented yet.