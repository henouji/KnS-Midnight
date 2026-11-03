using KnS.Server.Endpoints;

namespace KnS.Tests.Server;

public class AdditionFileEndpointTests
{
    [Fact]
    public void Trailer_matches_the_cdn_layout()
    {
        // struct: "1.4.0" padded to 16, "bgm.ogg" padded to 48, then (100 + 0x44) big-endian.
        byte[] trailer = AdditionFileEndpoint.BuildTrailer("1.4.0", "bgm.ogg", fileLength: 100);

        Assert.Equal(
            "312e342e30000000000000000000000062676d2e6f67670000000000000000000000000000000000000000000000000000000000000000000000000000000000000000a8",
            Convert.ToHexString(trailer).ToLowerInvariant());
    }

    [Theory]
    [InlineData("", 0, 167)]
    [InlineData("bytes=100-", 100, 167)]
    [InlineData("bytes=0-99", 0, 99)]
    [InlineData("bytes=50-9999", 50, 167)]
    [InlineData("bytes=-20", 0, 20)]
    [InlineData("items=5-6", 0, 167)]
    public void Ranges_are_clamped_to_the_content(string header, long start, long end)
    {
        Assert.Equal((start, end), AdditionFileEndpoint.ParseRange(header, totalLength: 168));
    }
}
