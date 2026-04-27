using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AthenaCompanion.Voice;

internal sealed class AthenaRealtimeSession : IAsyncDisposable
{
    private const string Model = "gpt-realtime-1.5";
    private readonly string _apiKey;
    private readonly string _instructions;
    private readonly string _voice;
    private readonly AthenaAudioInput _audioInput = new();
    private readonly AthenaAudioOutput _audioOutput = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private Channel<byte[]>? _audioChannel;
    private Task? _receiveTask;
    private Task? _sendAudioTask;
    private bool _started;

    public AthenaRealtimeSession(string apiKey, string instructions, string voice)
    {
        _apiKey = apiKey;
        _instructions = instructions;
        _voice = voice;
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

    private object CreateSessionUpdate() => new
    {
        type = "session.update",
        session = new
        {
            type = "realtime",
            model = Model,
            instructions = _instructions,
            audio = new
            {
                input = new
                {
                    turn_detection = new
                    {
                        type = "server_vad",
                        threshold = 0.5,
                        prefix_padding_ms = 300,
                        silence_duration_ms = 700
                    }
                },
                output = new
                {
                    voice = _voice
                }
            }
        }
    };

    private void OnAudioAvailable(object? sender, byte[] audio)
    {
        if (!_started || _audioChannel is null)
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
                        return;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                HandleServerEvent(builder.ToString());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"Realtime connection failed: {ex.Message}");
        }
    }

    private void HandleServerEvent(string json)
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
                _audioOutput.Clear();
                StatusChanged?.Invoke(this, "Listening");
                break;
            case "input_audio_buffer.speech_stopped":
                StatusChanged?.Invoke(this, "Thinking");
                break;
            case "response.created":
            case "response.output_item.added":
                StatusChanged?.Invoke(this, "Speaking");
                break;
            case "response.output_audio.delta":
            case "response.audio.delta":
                AddAudioDelta(root);
                break;
            case "response.done":
                StatusChanged?.Invoke(this, "Listening");
                break;
            case "error":
                Error?.Invoke(this, ReadError(root));
                break;
        }
    }

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

    private static string ReadError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) &&
            error.TryGetProperty("message", out var message))
        {
            return message.GetString() ?? "Realtime API error.";
        }

        return "Realtime API error.";
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
