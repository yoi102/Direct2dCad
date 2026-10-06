using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Direct2dCad.Agent;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent.Codex;

public sealed class CodexAppServerClient : ICodexAgentClient, IDisposable
{
    private const string CadToolNamespace = "direct2dcad";
    private const int NormalSafetyTokens = 640;
    private const int EstimatedThreadOverheadTokens = 256;
    private const string TruncatedContentMarker = "\n[content truncated to fit the model context window]";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICodexAppServerTransportFactory _transportFactory;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Connection? _connection;
    private ActiveTurn? _activeTurn;
    private string? _threadId;
    private string? _threadKey;
    private int _estimatedThreadTokens;
    private long _nextRequestId;
    private int _runInProgress;
    private bool _disposed;

    public CodexAppServerClient() : this(new CodexAppServerTransportFactory()) { }
    internal CodexAppServerClient(ICodexAppServerTransportFactory transportFactory) => _transportFactory = transportFactory;

    public async Task<IReadOnlyList<string>> GetModelsAsync(CodexAgentOptions options, CancellationToken cancellationToken = default)
    {
        var connection = await EnsureConnectedAsync(options, cancellationToken);
        var response = await SendRequestAsync(connection, "model/list", new { limit = 100, includeHidden = false }, cancellationToken);
        if (!response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];
        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("model", out var model) ? model.GetString() :
                item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(model => !string.IsNullOrWhiteSpace(model)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task<CodexAgentRunResult> RunAsync(CodexAgentRunRequest request,
        Func<AgentRunEvent, ValueTask>? reportEvent = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        if (Interlocked.CompareExchange(ref _runInProgress, 1, 0) != 0)
            throw new InvalidOperationException("A Codex turn is already running.");
        ActiveTurn? active = null;
        var completed = false;
        try
        {
            var connection = await EnsureConnectedAsync(request.Options, cancellationToken);
            await EnsureThreadAsync(request, connection, cancellationToken);
            active = new ActiveTurn(connection, _threadId!, request.Toolset, reportEvent,
                SynchronizationContext.Current, cancellationToken);
            _activeTurn = active;
            active.NotificationPump = PumpTurnNotificationsAsync(active);
            using var registration = active.Token.Register(() => active.Completion.TrySetCanceled(active.Token));
            var prompt = string.IsNullOrWhiteSpace(request.WorkspaceContext)
                ? request.Prompt
                : $"""
                   <cad_workspace_context>
                   {request.WorkspaceContext}
                   </cad_workspace_context>

                   <user_request>
                   {request.Prompt}
                   </user_request>
                   """;
            var contextWindow = Math.Clamp(
                request.Options.ContextWindowTokens,
                AiAssistantSettings.MinimumContextWindowTokens,
                AiAssistantSettings.MaximumContextWindowTokens);
            var maxOutputTokens = Math.Clamp(contextWindow / 8, 512, 2048);
            var contextReduced = false;
            var inputTokens = EstimateInputTokens(prompt, request.ContentParts);
            var freshThreadBudget = Math.Max(
                1024,
                contextWindow - EstimatedThreadOverheadTokens - maxOutputTokens - NormalSafetyTokens);
            if (_estimatedThreadTokens > 0 &&
                inputTokens <= freshThreadBudget &&
                _estimatedThreadTokens + inputTokens + maxOutputTokens + NormalSafetyTokens > contextWindow)
            {
                await ResetThreadAsync(connection, cancellationToken);
                await EnsureThreadAsync(request, connection, cancellationToken);
                active.ThreadId = _threadId!;
                contextReduced = true;
            }

            var availableTokens = Math.Max(
                1024,
                contextWindow - _estimatedThreadTokens - maxOutputTokens - NormalSafetyTokens);
            var limitedInput = inputTokens > availableTokens
                ? LimitInputToTokenBudget(prompt, request.ContentParts, availableTokens)
                : (Prompt: prompt, ContentParts: request.ContentParts);
            inputTokens = EstimateInputTokens(limitedInput.Prompt, limitedInput.ContentParts);
            contextReduced |= inputTokens < EstimateInputTokens(prompt, request.ContentParts);
            _estimatedThreadTokens += inputTokens + maxOutputTokens;
            if (contextReduced)
            {
                await ReportAsync(
                    active,
                    new AgentRunEvent(
                        AgentRunEventKind.ContextReduced,
                        ContextWindowTokens: contextWindow));
            }

            var turnResult = await SendRequestAsync(connection, "turn/start", new
            {
                threadId = active.ThreadId,
                input = CreateTurnInput(limitedInput.Prompt, limitedInput.ContentParts),
                model = NullIfWhiteSpace(request.Options.Model),
                effort = NormalizeReasoningEffort(request.Options.ReasoningEffort)
            }, active.Token);
            if (!turnResult.TryGetProperty("turn", out var turn) ||
                !turn.TryGetProperty("id", out var turnId) || string.IsNullOrWhiteSpace(turnId.GetString()))
                throw new InvalidOperationException("Codex app-server did not return a turn id.");
            active.TurnId = turnId.GetString();
            active.Ready.TrySetResult(true);
            await active.Completion.Task;
            active.Token.ThrowIfCancellationRequested();
            completed = true;
            return new CodexAgentRunResult(NullIfWhiteSpace(request.Options.Model), active.AssistantMessageCount == 0);
        }
        finally
        {
            if (active is not null)
            {
                active.Stop();
                if (!completed && !active.ProviderFinished && active.Connection.IsAlive)
                {
                    if (active.TurnId is { } turnId)
                        await InterruptTurnAsync(active.Connection, active.ThreadId, turnId);
                    else
                        FailConnection(active.Connection, new InvalidOperationException("The turn was cancelled before its id was received."));
                }
                // Keep the run gate closed until every accepted CAD request has actually exited.
                await active.DrainToolsAsync();
                await active.NotificationPump;
                Interlocked.CompareExchange(ref _activeTurn, null, active);
                active.Dispose();
            }
            Interlocked.Exchange(ref _runInProgress, 0);
        }
    }

    private static IReadOnlyList<object> CreateTurnInput(
        string prompt,
        IReadOnlyList<AiChatContentPart>? contentParts)
    {
        var input = new List<object> { new { type = "text", text = prompt } };
        foreach (var part in contentParts ?? [])
        {
            if (part.Type == AiChatContentPartType.Image)
            {
                input.Add(new
                {
                    type = "image",
                    url = part.DataUrl ?? throw new ArgumentException("Image content requires a data URL.")
                });
                continue;
            }

            if (part.Type == AiChatContentPartType.Text && part.FileName is not null)
                input.Add(new { type = "text", text = part.Text ?? string.Empty });
        }

        return input;
    }

    private static (string Prompt, IReadOnlyList<AiChatContentPart>? ContentParts) LimitInputToTokenBudget(
        string prompt,
        IReadOnlyList<AiChatContentPart>? contentParts,
        int tokenBudget)
    {
        if ((contentParts ?? []).Count(part => part.Type == AiChatContentPartType.Image) * 1024 + 8 >= tokenBudget)
            throw new InvalidOperationException("The image input cannot fit in the configured context window. Increase the context window or reduce the attached images.");
        var textLength = prompt.Length + (contentParts ?? [])
            .Where(part => part.Type == AiChatContentPartType.Text)
            .Sum(part => part.Text?.Length ?? 0);
        if (textLength == 0)
            return (prompt, contentParts);

        var low = 0;
        var high = textLength;
        while (low < high)
        {
            var keptCharacters = low + (high - low + 1) / 2;
            var candidate = CreateCharacterBudgetInput(prompt, contentParts, keptCharacters);
            if (EstimateInputTokens(candidate.Prompt, candidate.ContentParts) <= tokenBudget)
                low = keptCharacters;
            else
                high = keptCharacters - 1;
        }

        return CreateCharacterBudgetInput(prompt, contentParts, low);
    }

    private static (string Prompt, IReadOnlyList<AiChatContentPart>? ContentParts) CreateCharacterBudgetInput(
        string prompt,
        IReadOnlyList<AiChatContentPart>? contentParts,
        int characterBudget)
    {
        var remaining = characterBudget;
        var limitedPrompt = AllocateText(prompt, ref remaining);
        var limitedParts = contentParts?
            .Select(part => part.Type == AiChatContentPartType.Text
                ? part with { Text = AllocateText(part.Text ?? string.Empty, ref remaining) }
                : part)
            .ToArray();
        return (limitedPrompt, limitedParts);
    }

    private static string AllocateText(string text, ref int remaining)
    {
        var kept = Math.Min(text.Length, Math.Max(remaining, 0));
        remaining -= kept;
        if (kept == text.Length)
            return text;
        if (kept <= TruncatedContentMarker.Length + 8)
            return text[..kept];

        var contentCharacters = kept - TruncatedContentMarker.Length;
        var prefixLength = (int)(contentCharacters * 0.75);
        var suffixLength = contentCharacters - prefixLength;
        return string.Concat(
            text.AsSpan(0, prefixLength),
            TruncatedContentMarker,
            text.AsSpan(text.Length - suffixLength));
    }

    private static int EstimateInputTokens(
        string prompt,
        IReadOnlyList<AiChatContentPart>? contentParts)
    {
        var total = 8 + EstimateTextTokens(prompt);
        foreach (var part in contentParts ?? [])
            total += part.Type == AiChatContentPartType.Image
                ? 1024
                : EstimateTextTokens(part.Text);
        return total;
    }

    private static int EstimateTextTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var ascii = text.Count(character => character <= 0x7f);
        return (int)Math.Ceiling(ascii / 3.6 + (text.Length - ascii) / 1.25);
    }

    private static int EstimateThreadTokens(IReadOnlyList<AiToolDefinition> definitions) =>
        EstimatedThreadOverheadTokens + definitions.Sum(definition =>
            20 +
            EstimateTextTokens(definition.Name) +
            EstimateTextTokens(definition.Description) +
            EstimateTextTokens(definition.Parameters.GetRawText()));

    public async Task ResetConversationAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _runInProgress) != 0)
            throw new InvalidOperationException("Cannot reset a conversation while a Codex turn is running.");
        await ResetThreadAsync(_connection, cancellationToken);
    }

    private async Task ResetThreadAsync(Connection? connection, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try { await ResetThreadCoreAsync(connection, cancellationToken); }
        finally { _lifecycleGate.Release(); }
    }

    private async Task ResetThreadCoreAsync(Connection? connection, CancellationToken cancellationToken)
    {
        if (_threadId is not null && connection is { IsAlive: true })
        {
            try { await SendRequestAsync(connection, "thread/unsubscribe", new { threadId = _threadId }, cancellationToken); }
            catch (InvalidOperationException) { }
        }
        _threadId = null;
        _threadKey = null;
        _estimatedThreadTokens = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_connection is { } connection)
            FailConnection(connection, new ObjectDisposedException(nameof(CodexAppServerClient)));
        // Async operations can still be unwinding; their finally blocks own semaphore release.
    }

    private async Task<Connection> EnsureConnectedAsync(CodexAgentOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = $"{options.ExecutablePath.Trim()}|{NormalizeServiceTier(options.ServiceTier)}";
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is { IsAlive: true } existing && existing.Key == key)
                return existing;
            if (_connection is { } old)
                FailConnection(old, new InvalidOperationException("Codex app-server connection was reset."));
            var connection = new Connection(_transportFactory.Start(options), key);
            _connection = connection;
            _threadId = null;
            _threadKey = null;
            _estimatedThreadTokens = 0;
            connection.ReadLoop = ReadLoopAsync(connection);
            try
            {
                await SendRequestAsync(connection, "initialize", new
                {
                    clientInfo = new { name = "direct2dcad", title = "Direct2dCad", version = "1.0" },
                    capabilities = new { experimentalApi = true }
                }, cancellationToken);
                await WriteMessageAsync(connection, new { method = "initialized", @params = new { } }, cancellationToken);
                return connection;
            }
            catch (Exception exception) { FailConnection(connection, exception); throw; }
        }
        finally { _lifecycleGate.Release(); }
    }

    private async Task EnsureThreadAsync(CodexAgentRunRequest request, Connection connection, CancellationToken cancellationToken)
    {
        var definitions = request.Toolset?.ToolDefinitions ?? [];
        var key = JsonSerializer.Serialize(new
        {
            request.Options.Model,
            effort = NormalizeReasoningEffort(request.Options.ReasoningEffort),
            cwd = NormalizeWorkingDirectory(request.Options.WorkingDirectory),
            contextWindow = Math.Clamp(request.Options.ContextWindowTokens, AiAssistantSettings.MinimumContextWindowTokens, AiAssistantSettings.MaximumContextWindowTokens),
            definitions
        });
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            connection.Token.ThrowIfCancellationRequested();
            if (_threadId is not null && _threadKey == key) return;
            await ResetThreadCoreAsync(connection, cancellationToken);
            var response = await SendRequestAsync(connection, "thread/start", new
            {
                model = NullIfWhiteSpace(request.Options.Model),
                cwd = NormalizeWorkingDirectory(request.Options.WorkingDirectory),
                approvalPolicy = "never",
                sandbox = "read-only",
                ephemeral = true,
                developerInstructions = "You are the CAD assistant embedded in Direct2dCad. " +
                    "Use the provided dynamic CAD tools for every document inspection or modification. " +
                    "Do not use shell or file editing tools to change CAD documents. " +
                    "Never claim an operation succeeded before its CAD tool result confirms it.",
                dynamicTools = definitions.Select(definition => new
                {
                    name = definition.Name, @namespace = CadToolNamespace,
                    description = definition.Description, inputSchema = definition.Parameters, deferLoading = true
                }).ToArray()
            }, cancellationToken);
            if (!response.TryGetProperty("thread", out var thread) || !thread.TryGetProperty("id", out var id) ||
                string.IsNullOrWhiteSpace(id.GetString()))
                throw new InvalidOperationException("Codex app-server did not return a thread id.");
            _threadId = id.GetString();
            _threadKey = key;
            _estimatedThreadTokens = EstimateThreadTokens(definitions);
        }
        finally { _lifecycleGate.Release(); }
    }

    private async Task<JsonElement> SendRequestAsync(Connection connection, string method, object parameters, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.Token);
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = completion;
        try
        {
            await WriteMessageAsync(connection, new { method, id, @params = parameters }, linked.Token);
            return await completion.Task.WaitAsync(linked.Token);
        }
        finally { connection.Pending.TryRemove(id, out _); }
    }

    private async Task WriteMessageAsync(Connection connection, object message, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.Token);
        await _writeGate.WaitAsync(linked.Token);
        try { await connection.Transport.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions), linked.Token); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FailConnection(connection, exception);
            throw;
        }
        finally { _writeGate.Release(); }
    }

    private async Task ReadLoopAsync(Connection connection)
    {
        try
        {
            while (await connection.Transport.ReadLineAsync(connection.Token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement.Clone();
                if (message.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (message.TryGetProperty("method", out _)) DispatchServerRequest(connection, id, message);
                    else CompletePendingRequest(connection, id, message);
                    continue;
                }
                if (message.TryGetProperty("method", out var method) && _activeTurn is { } active &&
                    ReferenceEquals(active.Connection, connection))
                    active.Notifications.Writer.TryWrite((method.GetString(), message));
            }
            throw new InvalidOperationException($"Codex app-server closed its output stream. {connection.Transport.GetErrorSummary()}".Trim());
        }
        catch (OperationCanceledException) when (connection.Token.IsCancellationRequested) { }
        catch (Exception exception) { FailConnection(connection, exception); }
    }

    private static void CompletePendingRequest(Connection connection, long id, JsonElement message)
    {
        if (!connection.Pending.TryGetValue(id, out var completion)) return;
        if (message.TryGetProperty("error", out var error))
        {
            var detail = error.TryGetProperty("message", out var text) ? text.GetString() : error.GetRawText();
            completion.TrySetException(new InvalidOperationException($"Codex app-server: {detail}"));
        }
        else completion.TrySetResult(message.TryGetProperty("result", out var result) ? result.Clone() : JsonSerializer.SerializeToElement(new { }));
    }

    private void DispatchServerRequest(Connection connection, long id, JsonElement message)
    {
        var active = _activeTurn;
        if (active is null || !ReferenceEquals(active.Connection, connection) || !active.TryBeginTool(out var finished))
        {
            _ = ReplyToolFailureAsync(connection, id, "No active CAD turn accepts this request.");
            return;
        }
        _ = HandleServerRequestAsync(active, id, message, finished!);
    }

    private async Task HandleServerRequestAsync(ActiveTurn active, long id, JsonElement message, TaskCompletionSource finished)
    {
        try
        {
            await active.Ready.Task.WaitAsync(active.Token);
            if (message.GetProperty("method").GetString() != "item/tool/call")
                throw new InvalidOperationException("Unsupported server request.");
            var parameters = message.GetProperty("params");
            if (!MatchesTurn(active, parameters, nestedTurn: false))
                throw new InvalidOperationException("The CAD tool request belongs to another thread or turn.");
            if (!parameters.TryGetProperty("namespace", out var ns) || ns.GetString() != CadToolNamespace)
                throw new InvalidOperationException("Unsupported dynamic tool namespace.");
            if (active.Toolset is null) throw new InvalidOperationException("CAD tools are disabled.");
            var name = parameters.GetProperty("tool").GetString() ?? throw new InvalidOperationException("Missing CAD tool name.");
            var callId = parameters.GetProperty("callId").GetString() ?? throw new InvalidOperationException("Missing CAD tool call id.");
            var arguments = parameters.TryGetProperty("arguments", out var args) ? args.GetRawText() : "{}";
            await active.ToolGate.WaitAsync(active.Token);
            try
            {
                active.Token.ThrowIfCancellationRequested();
                var result = await InvokeAsync(active.SynchronizationContext, () =>
                {
                    active.Token.ThrowIfCancellationRequested();
                    return active.Toolset.ExecuteAsync(new AiToolCall(callId, name, arguments), active.Token);
                });
                active.Token.ThrowIfCancellationRequested();
                var content = AiToolResultContent.Parse(result);
                await ReportAsync(active, new AgentRunEvent(AgentRunEventKind.ToolResult, content.Text, name));
                var items = new List<object> { new { type = "inputText", text = content.Text } };
                items.AddRange(content.Images.Select(image => new { type = "inputImage", imageUrl = image.DataUrl }));
                await WriteMessageAsync(active.Connection, new { id, result = new { contentItems = items, success = IsSuccessfulToolResult(content.Text) } }, active.Token);
            }
            finally { active.ToolGate.Release(); }
        }
        catch (Exception exception) { await ReplyToolFailureAsync(active.Connection, id, $"Tool execution failed: {exception.Message}"); }
        finally { finished.TrySetResult(); }
    }

    private async Task ReplyToolFailureAsync(Connection connection, long id, string message)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await WriteMessageAsync(connection, new { id, result = new
            {
                contentItems = new[] { new { type = "inputText", text = message } }, success = false
            } }, timeout.Token);
        }
        catch (Exception) { /* Connection failure already invalidates its pending requests. */ }
    }

    private async Task PumpTurnNotificationsAsync(ActiveTurn active)
    {
        try
        {
            // Notifications can precede the turn/start reply. Buffer them without blocking the reader.
            await active.Ready.Task.WaitAsync(active.Token);
            await foreach (var (method, message) in active.Notifications.Reader.ReadAllAsync(active.Token))
            {
                if (!message.TryGetProperty("params", out var parameters) ||
                    !MatchesTurn(active, parameters, method == "turn/completed")) continue;
                if (method == "item/completed" && parameters.TryGetProperty("item", out var item) &&
                    item.TryGetProperty("type", out var type) && type.GetString() == "agentMessage" &&
                    item.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
                {
                    await ReportAsync(active, new AgentRunEvent(AgentRunEventKind.AssistantMessage, text.GetString()!.Trim()));
                    active.AssistantMessageCount++;
                }
                else if (method == "turn/completed")
                {
                    active.ProviderFinished = true;
                    var turn = parameters.GetProperty("turn");
                    var status = turn.TryGetProperty("status", out var state) ? state.GetString() : null;
                    if (status == "completed") active.Completion.TrySetResult(true);
                    else active.Completion.TrySetException(new InvalidOperationException(
                        $"Codex turn ended with status '{status}': {(turn.TryGetProperty("error", out var error) ? error.GetRawText() : status)}"));
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (active.Token.IsCancellationRequested) { }
        catch (Exception exception) { active.Completion.TrySetException(exception); }
    }

    private static bool MatchesTurn(ActiveTurn active, JsonElement parameters, bool nestedTurn)
    {
        if (!parameters.TryGetProperty("threadId", out var threadId) || threadId.ValueKind != JsonValueKind.String ||
            threadId.GetString() != active.ThreadId) return false;
        var turn = parameters;
        if (nestedTurn && !parameters.TryGetProperty("turn", out turn)) return false;
        return turn.TryGetProperty(nestedTurn ? "id" : "turnId", out var turnId) &&
               turnId.ValueKind == JsonValueKind.String && turnId.GetString() == active.TurnId;
    }

    private async Task InterruptTurnAsync(Connection connection, string threadId, string turnId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await SendRequestAsync(connection, "turn/interrupt", new { threadId, turnId }, timeout.Token);
        }
        catch (Exception exception)
        {
            // A rejected or unanswered interrupt cannot leave an unowned remote turn running.
            FailConnection(connection, new InvalidOperationException("Unable to interrupt the Codex turn; its connection was closed.", exception));
        }
    }

    private static async Task ReportAsync(ActiveTurn active, AgentRunEvent agentEvent)
    {
        active.Token.ThrowIfCancellationRequested();
        if (active.ReportEvent is null) return;
        await InvokeAsync(active.SynchronizationContext, async () =>
        {
            active.Token.ThrowIfCancellationRequested();
            await active.ReportEvent(agentEvent);
            return true;
        });
    }

    private static Task<T> InvokeAsync<T>(SynchronizationContext? synchronizationContext, Func<Task<T>> action)
    {
        if (synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, synchronizationContext)) return action();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizationContext.Post(async _ =>
        {
            try { completion.TrySetResult(await action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }, null);
        return completion.Task;
    }

    private void FailConnection(Connection connection, Exception exception)
    {
        if (_activeTurn is { } active && ReferenceEquals(active.Connection, connection))
            active.Completion.TrySetException(exception);
        connection.Fail(exception);
    }

    private static bool IsSuccessfulToolResult(string result)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind != JsonValueKind.Object ||
                   !document.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.False;
        }
        catch (JsonException) { return true; }
    }

    private static string NormalizeReasoningEffort(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "none" => "none",
            "minimal" => "minimal",
            "low" => "low",
            "high" => "high",
            "xhigh" => "xhigh",
            _ => "medium"
        };

    private static string NormalizeServiceTier(string value) =>
        string.Equals(value, "fast", StringComparison.OrdinalIgnoreCase) ? "fast" : "default";

    private static string NormalizeWorkingDirectory(string value) =>
        Directory.Exists(value) ? Path.GetFullPath(value) : Environment.CurrentDirectory;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class Connection(ICodexAppServerTransport transport, string key)
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _failed;
        public ICodexAppServerTransport Transport { get; } = transport;
        public string Key { get; } = key;
        public CancellationToken Token => _cancellation.Token;
        public bool IsAlive => Volatile.Read(ref _failed) == 0;
        public Task? ReadLoop { get; set; }
        public ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public void Fail(Exception exception)
        {
            if (Interlocked.Exchange(ref _failed, 1) != 0) return;
            foreach (var pending in Pending.Values) pending.TrySetException(exception);
            _cancellation.Cancel();
            try { Transport.Dispose(); }
            catch (Exception) { /* The connection is already invalidated; preserve its original failure. */ }
        }
    }

    private sealed class ActiveTurn : IDisposable
    {
        private readonly object _toolLock = new();
        private readonly List<Task> _tools = [];
        private readonly CancellationTokenSource _cancellation;
        private bool _stopping;
        public ActiveTurn(Connection connection, string threadId, IAgentToolset? toolset,
            Func<AgentRunEvent, ValueTask>? reportEvent, SynchronizationContext? synchronizationContext, CancellationToken token)
        {
            Connection = connection; ThreadId = threadId; Toolset = toolset;
            ReportEvent = reportEvent; SynchronizationContext = synchronizationContext;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, connection.Token);
        }
        public Connection Connection { get; }
        public string ThreadId { get; set; }
        public string? TurnId { get; set; }
        public IAgentToolset? Toolset { get; }
        public Func<AgentRunEvent, ValueTask>? ReportEvent { get; }
        public SynchronizationContext? SynchronizationContext { get; }
        public CancellationToken Token => _cancellation.Token;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<(string? Method, JsonElement Message)> Notifications { get; } = Channel.CreateUnbounded<(string?, JsonElement)>();
        public Task NotificationPump { get; set; } = Task.CompletedTask;
        public SemaphoreSlim ToolGate { get; } = new(1, 1);
        public int AssistantMessageCount { get; set; }
        public bool ProviderFinished { get; set; }
        public bool TryBeginTool(out TaskCompletionSource? finished)
        {
            lock (_toolLock)
            {
                finished = null;
                if (_stopping || Completion.Task.IsCompleted || Token.IsCancellationRequested) return false;
                finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _tools.Add(finished.Task);
                return true;
            }
        }
        public void Stop()
        {
            lock (_toolLock) _stopping = true;
            _cancellation.Cancel();
            Notifications.Writer.TryComplete();
        }
        public Task DrainToolsAsync() { lock (_toolLock) return Task.WhenAll(_tools); }
        public void Dispose() { _cancellation.Dispose(); ToolGate.Dispose(); }
    }
}
