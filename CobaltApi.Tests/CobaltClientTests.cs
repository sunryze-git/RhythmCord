using Xunit;
using CobaltApi;

namespace CobaltApi.Tests
{
    public class CobaltClientTests
    {
        [Fact]
        public void BuildRequestContent_SerializesRequest()
        {
            var request = new Request { /* set properties as needed */ };
            var content = typeof(CobaltClient)
                .GetMethod("BuildRequestContent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { request });
            Assert.NotNull(content);
        }
    }
}

