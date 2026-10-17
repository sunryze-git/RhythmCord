using System.Net;
using Singularity.Features.Music.Resolvers;

namespace Singularity.Tests;

public class QueryUtilTests
{
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)] // cloud metadata
    [InlineData("100.64.0.1", false)]      // CGNAT
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("93.184.216.34", true)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void IsPublicAddress_classifies_ranges(string ip, bool expected) =>
        Assert.Equal(expected, QueryUtil.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("http://localhost/x")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://printer.internal/x")]
    [InlineData("http://thing.local/x")]
    public async Task Private_hosts_are_rejected(string url) =>
        Assert.False(await QueryUtil.IsPublicHostAsync(new Uri(url)));

    [Fact]
    public async Task Public_literal_ip_is_accepted() =>
        Assert.True(await QueryUtil.IsPublicHostAsync(new Uri("http://8.8.8.8/")));

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("just words")]
    public void Only_http_and_https_urls_count_as_web_uris(string query) =>
        Assert.False(QueryUtil.TryGetWebUri(query, out _));

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("  http://example.com/a  ")]
    public void Http_urls_are_accepted(string query) => Assert.True(QueryUtil.TryGetWebUri(query, out _));
}
