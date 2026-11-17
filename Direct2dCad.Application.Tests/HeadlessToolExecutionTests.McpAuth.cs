using System.Net;
using System.Net.Http.Headers;
using Direct2dCad.Application.Platform;
using Direct2dCad.Mcp;

namespace Direct2dCad.Application.Tests;

public sealed partial class HeadlessToolExecutionTests
{
    private const string InitializeRequest =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"auth-test","version":"1.0"}}}""";

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token")]
    [InlineData("Bearer secret-token-extra")]
    [InlineData("Basic c2VjcmV0LXRva2Vu")]
    public async Task McpWithAnAccessTokenRejectsRequestsWithoutIt(string? authorization)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(
            0, uiContext, workspace, accessToken: "secret-token", cancellationToken: timeout.Token);

        using var response = await PostInitializeAsync(server.Endpoint, authorization, timeout.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Empty(workspace.GetDocuments());
    }

    [Theory]
    [InlineData("Bearer secret-token")]
    [InlineData("bearer secret-token")]
    public async Task McpWithAnAccessTokenAcceptsTheMatchingBearerHeader(string authorization)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(
            0, uiContext, workspace, accessToken: "secret-token", cancellationToken: timeout.Token);

        using var response = await PostInitializeAsync(server.Endpoint, authorization, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("secret-token", server.AccessToken);
    }

    [Fact]
    public async Task McpWithoutAnAccessTokenNeedsNoAuthorizationHeader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();
        await using var server = await CadMcpServer.StartAsync(0, uiContext, workspace, cancellationToken: timeout.Token);

        using var response = await PostInitializeAsync(server.Endpoint, null, timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(server.AccessToken);
    }

    [Fact]
    public async Task McpRefusesToStartWithATokenThatCannotTravelInAHeader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var workspace = new HeadlessWorkspace();
        using var uiContext = new McpUiContext();

        await Assert.ThrowsAsync<ArgumentException>(() => CadMcpServer.StartAsync(
            0, uiContext, workspace, accessToken: "has space", cancellationToken: timeout.Token));
    }

    [Fact]
    public void McpAccessTokenComesFromTheCommandLineBeforeTheEnvironment()
    {
        string? Environment(string name) => name == CadMcpServer.AccessTokenEnvironmentVariable ? " from-env " : null;

        Assert.Null(CadMcpServer.GetRequestedAccessToken([], _ => null));
        Assert.Equal("from-env", CadMcpServer.GetRequestedAccessToken(["--mcp"], Environment));
        Assert.Equal("from-arg", CadMcpServer.GetRequestedAccessToken(["--mcp-token", "from-arg"], Environment));
        Assert.Throws<ArgumentException>(() => CadMcpServer.GetRequestedAccessToken(["--mcp-token"], _ => null));
        Assert.Throws<ArgumentException>(() => CadMcpServer.GetRequestedAccessToken(["--mcp-token", "a b"], _ => null));
        Assert.Throws<ArgumentException>(() => CadMcpServer.GetRequestedAccessToken([], _ => "badétoken"));
    }

    [Fact]
    public void GeneratedAccessTokensAreUniqueAndHeaderSafe()
    {
        var first = CadMcpAccessToken.Generate();
        var second = CadMcpAccessToken.Generate();

        Assert.NotEqual(first, second);
        Assert.True(CadMcpAccessToken.IsValid(first));
        Assert.Equal(43, first.Length);
        Assert.DoesNotContain(first, character => character is '+' or '/' or '=');
    }

    private static async Task<HttpResponseMessage> PostInitializeAsync(
        Uri endpoint,
        string? authorization,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(InitializeRequest, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (authorization is not null)
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
