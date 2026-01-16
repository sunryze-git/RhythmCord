using System.Threading.Tasks;
using Xunit;
using CobaltApi;

namespace CobaltApi.Tests
{
    public class CobaltClientTests
    {
        [Fact]
        public void BuildRequest_CreatesHttpRequestWithCorrectHeaders()
        {
            var request = new Request { url = "https://example.com" };
            var httpRequest = CobaltClient.BuildRequest(request);
            Assert.NotNull(httpRequest);
            Assert.Equal(System.Net.Http.HttpMethod.Post, httpRequest.Method);
            Assert.Contains(httpRequest.Headers.Accept, h => h.MediaType == "application/json");
            Assert.NotNull(httpRequest.Content);
        }

        [Fact]
        public async Task BuildRequestContent_SerializesRequestWithDefaults()
        {
            var request = new Request { url = "https://example.com" };
            var content = CobaltClient.BuildRequestContent(request);
            Assert.NotNull(content);
            var json = await content.ReadAsStringAsync();
            Assert.Contains("\"url\":\"https://example.com\"", json);
            Assert.Contains("\"audioBitrate\":\"128\"", json); // default value
        }

        [Fact]
        public async Task HandleResponseAsync_ThrowsOnUnknownStatus()
        {
            var responseJson = "{\"status\":\"unknown\"}";
            var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
            var ex = await Assert.ThrowsAsync<Newtonsoft.Json.JsonException>(() => CobaltClient.HandleResponseAsync(response));
            Assert.Contains("Unknown response status", ex.Message);
        }

        [Fact]
        public void BuildRequestContent_SerializesRequest()
        {
            var request = new Request { /* set properties as needed */ };
            var content = CobaltClient.BuildRequestContent(request);
            Assert.NotNull(content);
        }
    }
}
