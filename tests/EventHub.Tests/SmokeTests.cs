using System.Net;

namespace EventHub.Tests;

public class SmokeTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task Page_returns_ok(string url)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
