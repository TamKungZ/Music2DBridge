using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Music2DBridge.VTubeStudio;

public sealed class VtsClient : IAsyncDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = null, WriteIndented = false };

    public async Task ConnectAsync(Uri endpoint, CancellationToken ct)
    {
        await _ws.ConnectAsync(endpoint, ct);
    }

    public async Task AuthenticateAsync(string pluginName, string pluginDeveloper, string pluginIconBase64, CancellationToken ct)
    {
        string? token = null;

        if (!string.IsNullOrWhiteSpace(TokenStorePath) && File.Exists(TokenStorePath))
        {
            token = (await File.ReadAllTextAsync(TokenStorePath, ct)).Trim();
        }

        if (!string.IsNullOrWhiteSpace(token) && await TryAuthenticateWithTokenAsync(pluginName, pluginDeveloper, token, ct))
        {
            return;
        }

        var tokenRequest = Envelope("AuthenticationTokenRequest", new
        {
            pluginName,
            pluginDeveloper,
            pluginIcon = pluginIconBase64
        });

        await SendJsonAsync(tokenRequest, ct);
        using var tokenResp = await ReceiveJsonAsync(ct);

        token = tokenResp.RootElement.GetProperty("data").GetProperty("authenticationToken").GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Failed to receive authentication token from VTube Studio.");
        }

        if (!await TryAuthenticateWithTokenAsync(pluginName, pluginDeveloper, token, ct))
        {
            throw new InvalidOperationException("Authentication rejected by VTube Studio.");
        }

        if (!string.IsNullOrWhiteSpace(TokenStorePath))
        {
            var tokenDirectory = Path.GetDirectoryName(TokenStorePath);
            if (!string.IsNullOrWhiteSpace(tokenDirectory))
            {
                Directory.CreateDirectory(tokenDirectory);
            }

            await File.WriteAllTextAsync(TokenStorePath, token, ct);
        }
    }

    public string? TokenStorePath { get; set; }

    private async Task<bool> TryAuthenticateWithTokenAsync(string pluginName, string pluginDeveloper, string token, CancellationToken ct)
    {
        
        var authRequest = Envelope("AuthenticationRequest", new
        {
            pluginName,
            pluginDeveloper,
            authenticationToken = token
        });

        await SendJsonAsync(authRequest, ct);
        using var authResp = await ReceiveJsonAsync(ct);

        var ok = authResp.RootElement.GetProperty("data").GetProperty("authenticated").GetBoolean();
        return ok;
    }

    public async Task InjectParametersAsync(IEnumerable<(string Id, double Value, double Weight)> parameters, CancellationToken ct)
    {
        var values = parameters.Select(p => new { id = p.Id, value = p.Value, weight = p.Weight }).ToArray();

        var request = Envelope("InjectParameterDataRequest", new
        {
            faceFound = true,
            mode = "set",
            parameterValues = values
        });

        await SendJsonAsync(request, ct);
        using var _ = await ReceiveJsonAsync(ct);
    }

    public async Task EnsureParameterAsync(string parameterId, double min, double max, double defaultValue, string explanation, CancellationToken ct)
    {
        var request = Envelope("ParameterCreationRequest", new
        {
            parameterName = parameterId,
            explanation,
            min,
            max,
            defaultValue
        });

        await SendJsonAsync(request, ct);
        using var _ = await ReceiveJsonAsync(ct);
    }

    private object Envelope(string messageType, object data) => new
    {
        apiName = "VTubeStudioPublicAPI",
        apiVersion = "1.0",
        requestID = Guid.NewGuid().ToString("N"),
        messageType,
        data
    };

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private async Task<JsonDocument> ReceiveJsonAsync(CancellationToken ct)
    {
        var buf = new byte[32 * 1024];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(buf, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("WebSocket closed by server.");
            }

            ms.Write(buf, 0, result.Count);
        }
        while (!result.EndOfMessage);

        ms.Position = 0;
        return await JsonDocument.ParseAsync(ms, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);
        }

        _ws.Dispose();
    }
}
