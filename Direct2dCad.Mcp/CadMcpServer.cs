using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Application.Platform;
using Direct2dCad.Application.Tools;
using MessagePipe;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace Direct2dCad.Mcp;

/// <summary>Hosts the live desktop workspace over loopback MCP Streamable HTTP.</summary>
public sealed class CadMcpServer : IAsyncDisposable
{
    public const int DefaultPort = 5277;
    public const string AccessTokenEnvironmentVariable = "DIRECT2DCAD_MCP_TOKEN";

    // Annotation sets are keyed by tool name; CadMcpAnnotationTests keeps them in step with the tool catalog.
    internal static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "get_agent_capabilities", "list_documents", "get_document_summary", "get_view_settings",
        "get_entity_statistics", "list_entities", "list_layers", "get_entity_geometry",
        "list_styles", "list_system_fonts", "list_blocks", "list_document_catalog",
        "measure_geometry", "capture_view", "list_layouts", "list_document_templates", "list_recovery_copies", "get_file_revision",
        "get_application_settings"
    };

    internal static readonly HashSet<string> AdditiveTools = new(StringComparer.Ordinal)
    {
        "create_document", "open_document", "open_dxf", "open_recovery_copy", "create_layer", "create_layout",
        "create_graphic_style", "create_text_style", "create_fill_style", "create_hatch_pattern", "create_line_type",
        "insert_block", "insert_image_from_file", "insert_image_from_clipboard", "duplicate_entities", "array_entities"
    };

    /// <summary>Tools that read or write outside the open drawing (files, clipboard, system fonts, app settings).</summary>
    internal static readonly HashSet<string> OpenWorldTools = new(StringComparer.Ordinal)
    {
        "open_document", "open_dxf", "save_document", "close_document", "export_dxf", "insert_image_from_file",
        "print_document", "list_system_fonts", "get_file_revision", "save_compatible_copy", "insert_image_from_clipboard",
        "list_recovery_copies", "open_recovery_copy", "clear_recovery_copies", "get_application_settings", "set_application_settings"
    };

    internal static string ServerVersion { get; } =
        typeof(CadMcpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private readonly ICadToolWorkspace _workspace;
    private readonly SynchronizationContext _uiContext;
    private readonly IImageImportService? _imageImportService;
    private readonly IAsyncPublisher<CadToolActivityMessage>? _activityPublisher;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly WebApplication _application;
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _toolResponses = new();
    private readonly byte[]? _accessTokenHash;

    private CadMcpServer(
        int port,
        SynchronizationContext uiContext,
        ICadToolWorkspace workspace,
        IImageImportService? imageImportService,
        IAsyncPublisher<CadToolActivityMessage>? activityPublisher,
        string? accessToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _imageImportService = imageImportService;
        _activityPublisher = activityPublisher;
        if (!string.IsNullOrEmpty(accessToken))
        {
            if (!CadMcpAccessToken.IsValid(accessToken))
                throw new ArgumentException("The MCP access token must be 1-512 visible ASCII characters without spaces.", nameof(accessToken));
            AccessToken = accessToken;
            _accessTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        }

        var tools = CadWorkspaceToolExecutor.ToolDefinitions.Select(definition => new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = definition.Parameters.Clone(),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = ReadOnlyTools.Contains(definition.Name),
                DestructiveHint = !ReadOnlyTools.Contains(definition.Name) && !AdditiveTools.Contains(definition.Name) &&
                    !definition.Name.StartsWith("add_", StringComparison.Ordinal),
                OpenWorldHint = OpenWorldTools.Contains(definition.Name)
            }
        }).ToList();

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "Direct2dCad", Version = ServerVersion };
            options.ServerInstructions = "Control the open Direct2dCad workspace. Start with list_documents and " +
                "get_agent_capabilities. Coordinates are millimetres and angles are counter-clockwise degrees. " +
                "Supply document_id when working across documents. Each MCP tool call has its own undo batch. " +
                "Use add_entities for a multi-part drawing in one undo step. Inspect tool results after edits.";
        })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients)
            .WithListToolsHandler((request, _) =>
            {
                if (request.Params?.Cursor is not null)
                    throw new McpProtocolException("The tool catalog has no continuation cursor.", McpErrorCode.InvalidParams);
                return ValueTask.FromResult(new ListToolsResult { Tools = tools });
            })
            .WithCallToolHandler(async (request, token) =>
            {
                var disconnect = request.Services!.GetRequiredService<IHttpContextAccessor>().HttpContext?.RequestAborted
                    ?? CancellationToken.None;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, disconnect);
                return await CallToolAsync(request.Params!, cancellation.Token);
            });

        _application = builder.Build();
        _application.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            var origin = context.Request.Headers.Origin.ToString();
            var validHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1";
            var validOrigin = string.IsNullOrEmpty(origin) ||
                (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme == "http" &&
                 uri.IsLoopback && uri.Port == context.Connection.LocalPort);
            if (!validHost || !validOrigin)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (_accessTokenHash is not null && !HasValidBearerToken(context.Request.Headers.Authorization.ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer realm=\"Direct2dCad MCP\"";
                return;
            }
            if (HttpMethods.IsPost(context.Request.Method))
            {
                // Validate the raw body before the SDK converts arguments to a dictionary and loses duplicate keys.
                context.Request.EnableBuffering();
                try
                {
                    using var requestDocument = await JsonDocument.ParseAsync(context.Request.Body,
                        new JsonDocumentOptions { AllowDuplicateProperties = false }, context.RequestAborted);
                    if (requestDocument.RootElement.ValueKind == JsonValueKind.Object &&
                        requestDocument.RootElement.TryGetProperty("method", out var method) &&
                        method.ValueKind == JsonValueKind.String && method.GetString() == "tools/call")
                    {
                        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        _toolResponses[context.TraceIdentifier] = completed;
                        void CompleteResponse()
                        {
                            _toolResponses.TryRemove(context.TraceIdentifier, out _);
                            completed.TrySetResult();
                        }
                        var aborted = context.RequestAborted.Register(CompleteResponse);
                        context.Response.OnCompleted(() =>
                        {
                            CompleteResponse();
                            aborted.Dispose();
                            return Task.CompletedTask;
                        });
                    }
                }
                catch (JsonException)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        """{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Request JSON must be valid and contain unique property names."}}""",
                        context.RequestAborted);
                    return;
                }
                context.Request.Body.Position = 0;
            }
            await next(context);
        });
        _application.MapMcp("/mcp");
    }

    public Uri Endpoint => new(new Uri(_application.Urls.Single()), "/mcp");

    /// <summary>The bearer token clients must present, or null when the loopback checks alone apply.</summary>
    public string? AccessToken { get; }

    private bool HasValidBearerToken(string authorization)
    {
        const string scheme = "Bearer ";
        if (!authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;
        // Hashing first gives equal-length inputs, so the comparison time reveals neither content nor length.
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[scheme.Length..].Trim()));
        return CryptographicOperations.FixedTimeEquals(presented, _accessTokenHash);
    }

    internal async Task DrainToolResponsesAsync(CancellationToken cancellationToken)
    {
        var responses = _toolResponses.Values.Select(response => response.Task).ToArray();
        try
        {
            await Task.WhenAll(responses).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A stalled client cannot indefinitely prevent a UI-requested stop or port change.
        }
    }

    /// <summary>Returns null when MCP was not requested. --mcp uses the default port.</summary>
    public static int? GetRequestedPort(IReadOnlyList<string> arguments)
    {
        int? port = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "--mcp")
                port ??= DefaultPort;
            if (arguments[index] != "--mcp-port")
                continue;
            if (++index >= arguments.Count ||
                !int.TryParse(arguments[index], NumberStyles.None, CultureInfo.InvariantCulture, out var requested) ||
                requested is < 1 or > 65535)
                throw new ArgumentException("--mcp-port requires a port between 1 and 65535.");
            port = requested;
        }
        return port;
    }

    /// <summary>
    /// Returns the token from --mcp-token, else from the DIRECT2DCAD_MCP_TOKEN environment variable,
    /// else null. The environment variable keeps the secret out of the visible process command line.
    /// </summary>
    public static string? GetRequestedAccessToken(
        IReadOnlyList<string> arguments,
        Func<string, string?>? readEnvironment = null)
    {
        string? token = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] != "--mcp-token")
                continue;
            if (++index >= arguments.Count ||
                !CadMcpAccessToken.IsValid(arguments[index]))
                throw new ArgumentException("--mcp-token requires 1-512 visible ASCII characters without spaces.");
            token = arguments[index];
        }
        if (token is not null)
            return token;
        var fromEnvironment = (readEnvironment ?? Environment.GetEnvironmentVariable)(AccessTokenEnvironmentVariable)?.Trim();
        return string.IsNullOrEmpty(fromEnvironment) ? null :
            CadMcpAccessToken.IsValid(fromEnvironment) ? fromEnvironment :
            throw new ArgumentException($"{AccessTokenEnvironmentVariable} must be 1-512 visible ASCII characters without spaces.");
    }

    public static async Task<CadMcpServer> StartAsync(
        int port,
        SynchronizationContext uiContext,
        ICadToolWorkspace workspace,
        IImageImportService? imageImportService = null,
        IAsyncPublisher<CadToolActivityMessage>? activityPublisher = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        var server = new CadMcpServer(port, uiContext, workspace, imageImportService, activityPublisher, accessToken);
        try
        {
            await server._application.StartAsync(cancellationToken).ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken token)
    {
        if (!CadWorkspaceToolExecutor.ToolDefinitions.Any(tool => tool.Name == request.Name))
            throw new McpProtocolException($"Unknown CAD tool: {request.Name}", McpErrorCode.InvalidParams);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await _commands.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var arguments = new JsonObject();
            foreach (var argument in request.Arguments?.AsEnumerable() ?? [])
                arguments[argument.Key] = JsonNode.Parse(argument.Value.GetRawText());
            var call = new AiToolCall(Guid.NewGuid().ToString("N"), request.Name, arguments.ToJsonString());
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationToken = linked.Token;
            var dispatchState = 0;
            using var pendingCancellation = cancellationToken.Register(() =>
            {
                if (Interlocked.CompareExchange(ref dispatchState, 2, 0) == 0)
                    completion.TrySetCanceled(cancellationToken);
            });
            using var shutdownCancellation = _shutdown.Token.Register(() =>
                completion.TrySetCanceled(_shutdown.Token));
            _uiContext.Post(async _ =>
            {
                if (Interlocked.CompareExchange(ref dispatchState, 1, 0) != 0)
                    return;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // A new executor resolves the current active document and starts a fresh undo batch.
                    var executor = new CadWorkspaceToolExecutor(_workspace, _imageImportService, _activityPublisher);
                    completion.TrySetResult(await executor.ExecuteAsync(call, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, null);

            var result = AiToolResultContent.Parse(await completion.Task.ConfigureAwait(false));
            using var document = JsonDocument.Parse(result.Text);
            var content = new List<ContentBlock> { new TextContentBlock { Text = result.Text } };
            foreach (var image in result.Images)
            {
                var dataUrl = image.DataUrl!;
                var separator = dataUrl.IndexOf(';');
                content.Add(new ImageContentBlock
                {
                    MimeType = dataUrl[5..separator],
                    Data = Encoding.UTF8.GetBytes(dataUrl[(dataUrl.IndexOf(',') + 1)..])
                });
            }
            return new CallToolResult
            {
                Content = content,
                StructuredContent = document.RootElement.Clone(),
                IsError = document.RootElement.TryGetProperty("success", out var success) &&
                          success.ValueKind == JsonValueKind.False
            };
        }
        finally
        {
            _commands.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _application.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        finally
        {
            await _application.DisposeAsync().ConfigureAwait(false);
        }
    }
}
