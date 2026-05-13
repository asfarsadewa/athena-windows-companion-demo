using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AthenaCompanion.Tools;

namespace AthenaCompanion.Voice;

internal sealed class AthenaRealtimeSession : IAsyncDisposable
{
    internal const string Model = "gpt-realtime-2";
    internal const string ReasoningEffort = "low";
    private readonly string _apiKey;
    private readonly string _instructions;
    private readonly string _voice;
    private readonly AthenaToolExecutor _toolExecutor;
    private readonly AthenaAudioInput _audioInput = new();
    private readonly AthenaAudioOutput _audioOutput = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Channel<byte[]>? _audioChannel;
    private Task? _receiveTask;
    private Task? _sendAudioTask;
    private readonly HashSet<string> _handledToolCallIds = [];
    private volatile bool _audioInputSuspended;
    private volatile bool _responseAudioActive;
    private bool _isSpeaking;
    private bool _started;

    public AthenaRealtimeSession(string apiKey, string instructions, string voice, AthenaToolExecutor toolExecutor)
    {
        _apiKey = apiKey;
        _instructions = instructions;
        _voice = voice;
        _toolExecutor = toolExecutor;
        _audioInput.AudioAvailable += OnAudioAvailable;
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? Error;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _audioChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _socket = new ClientWebSocket();
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        _socket.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

        var uri = new Uri($"wss://api.openai.com/v1/realtime?model={Model}");
        StatusChanged?.Invoke(this, "Connecting...");
        await _socket.ConnectAsync(uri, cancellationToken);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _sendAudioTask = Task.Run(() => SendAudioLoopAsync(_cts.Token));

        await SendJsonAsync(CreateSessionUpdate(), cancellationToken);
        _audioOutput.Start();
        _audioInput.Start();
        StatusChanged?.Invoke(this, "Listening");
    }

    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _audioInput.Stop();
        _audioOutput.Clear();
        _audioChannel?.Writer.TryComplete();

        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await SendJsonAsync(new { type = "response.cancel" }, CancellationToken.None);
            }
            catch
            {
                // Best effort only while shutting down.
            }
        }

        _cts?.Cancel();

        if (_socket is { State: WebSocketState.Open or WebSocketState.CloseReceived })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Athena voice stopped", CancellationToken.None);
            }
            catch
            {
                // Closing is best effort; dispose below guarantees cleanup.
            }
        }

        _socket?.Dispose();
        _socket = null;
        _cts?.Dispose();
        _cts = null;
        StatusChanged?.Invoke(this, "Voice off");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _audioInput.Dispose();
        _audioOutput.Dispose();
        _sendLock.Dispose();
    }

    private object CreateSessionUpdate() => CreateSessionUpdatePayload(_instructions, _voice);

    internal static object CreateSessionUpdatePayload(string instructions, string voice) => new
    {
        type = "session.update",
        session = new
        {
            type = "realtime",
            model = Model,
            instructions,
            reasoning = new
            {
                effort = ReasoningEffort
            },
            audio = new
            {
                input = new
                {
                    format = new
                    {
                        type = "audio/pcm",
                        rate = AthenaAudioInput.SampleRate
                    },
                    noise_reduction = new
                    {
                        type = "far_field"
                    },
                    turn_detection = new
                    {
                        type = "server_vad",
                        threshold = 0.45,
                        prefix_padding_ms = 300,
                        silence_duration_ms = 850
                    }
                },
                output = new
                {
                    format = new
                    {
                        type = "audio/pcm",
                        rate = AthenaAudioInput.SampleRate
                    },
                    voice
                }
            },
            tools = AthenaToolDefinitions.Create(strict: false),
            tool_choice = "auto"
        }
    };

    private void OnAudioAvailable(object? sender, byte[] audio)
    {
        if (!_started || _audioInputSuspended || _responseAudioActive || _audioChannel is null)
        {
            return;
        }

        _audioChannel.Writer.TryWrite(audio);
    }

    private async Task SendAudioLoopAsync(CancellationToken cancellationToken)
    {
        if (_audioChannel is null)
        {
            return;
        }

        try
        {
            await foreach (var audio in _audioChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await SendJsonAsync(new
                {
                    type = "input_audio_buffer.append",
                    audio = Convert.ToBase64String(audio)
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex) when (IsRemoteCloseException(ex))
        {
            HandleRemoteDisconnect();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"Microphone streaming failed: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var builder = new StringBuilder();

        try
        {
            while (_socket is { State: WebSocketState.Open } && !cancellationToken.IsCancellationRequested)
            {
                builder.Clear();
                WebSocketReceiveResult result;

                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        HandleRemoteDisconnect();
                        return;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                await HandleServerEventAsync(builder.ToString(), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex) when (IsRemoteCloseException(ex))
        {
            HandleRemoteDisconnect();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"Realtime connection failed: {ex.Message}");
        }
    }

    private async Task HandleServerEventAsync(string json, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var type = typeElement.GetString();
        switch (type)
        {
            case "session.created":
            case "session.updated":
                StatusChanged?.Invoke(this, "Listening");
                break;
            case "input_audio_buffer.speech_started":
                _responseAudioActive = false;
                _audioOutput.Clear();
                _isSpeaking = false;
                StatusChanged?.Invoke(this, "Listening");
                break;
            case "input_audio_buffer.speech_stopped":
                _isSpeaking = false;
                StatusChanged?.Invoke(this, "Thinking");
                break;
            case "response.created":
                _isSpeaking = false;
                StatusChanged?.Invoke(this, "Thinking");
                break;
            case "response.output_item.added":
                _isSpeaking = false;
                StatusChanged?.Invoke(this, RealtimeEventParser.IsFunctionCallEvent(root) ? "Using tool" : "Thinking");
                break;
            case "response.output_item.done":
                if (RealtimeEventParser.TryReadFunctionCallFromItemEvent(root, out var itemCall))
                {
                    await HandleFunctionCallAsync(itemCall, cancellationToken);
                }

                break;
            case "response.function_call_arguments.done":
                if (RealtimeEventParser.TryReadFunctionCallFromProperties(root, out var argumentsCall))
                {
                    await HandleFunctionCallAsync(argumentsCall, cancellationToken);
                }

                break;
            case "response.output_audio.delta":
            case "response.audio.delta":
                if (!_isSpeaking)
                {
                    _isSpeaking = true;
                    StatusChanged?.Invoke(this, "Speaking");
                }

                _responseAudioActive = true;
                AddAudioDelta(root);
                break;
            case "response.done":
                _responseAudioActive = false;
                _isSpeaking = false;
                StatusChanged?.Invoke(this, "Listening");
                break;
            case "error":
                _responseAudioActive = false;
                Error?.Invoke(this, RealtimeEventParser.ReadError(root));
                break;
        }
    }

    private async Task HandleFunctionCallAsync(RealtimeFunctionCall call, CancellationToken cancellationToken)
    {
        if (!_handledToolCallIds.Add(call.CallId))
        {
            return;
        }

        StatusChanged?.Invoke(this, "Using tool");
        _audioInputSuspended = true;
        AthenaToolResult result;
        try
        {
            result = await _toolExecutor.ExecuteAsync(call.Name, call.Arguments, cancellationToken);
        }
        finally
        {
            _audioInputSuspended = false;
        }

        if (result.StopVoice)
        {
            _audioInputSuspended = true;
            _responseAudioActive = false;
            _audioOutput.Clear();
            StatusChanged?.Invoke(this, "Music mode");
        }

        if (!result.ContinueVoiceResponse)
        {
            return;
        }

        await SendJsonAsync(new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "function_call_output",
                call_id = call.CallId,
                output = result.Output
            }
        }, cancellationToken);

        StatusChanged?.Invoke(this, "Thinking");
        await SendJsonAsync(new
        {
            type = "response.create",
            response = new
            {
                instructions = "Speak the tool result naturally and briefly as Athena."
            }
        }, cancellationToken);
    }

    private void HandleRemoteDisconnect()
    {
        if (!_started)
        {
            return;
        }

        _audioInputSuspended = true;
        _responseAudioActive = false;
        _audioChannel?.Writer.TryComplete();
        _cts?.Cancel();
        StatusChanged?.Invoke(this, "Disconnected");
    }

    private static bool IsRemoteCloseException(WebSocketException ex) =>
        ex.WebSocketErrorCode is WebSocketError.ConnectionClosedPrematurely or WebSocketError.InvalidState ||
        ex.Message.Contains("remote party closed", StringComparison.OrdinalIgnoreCase);

    private void AddAudioDelta(JsonElement root)
    {
        if (!root.TryGetProperty("delta", out var deltaElement))
        {
            return;
        }

        var delta = deltaElement.GetString();
        if (string.IsNullOrWhiteSpace(delta))
        {
            return;
        }

        _audioOutput.AddPcm16(Convert.FromBase64String(delta));
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        if (_socket is not { State: WebSocketState.Open })
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
