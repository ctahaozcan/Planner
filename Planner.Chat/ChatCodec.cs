using System.Text.Json;
using System.Text.Json.Serialization;

namespace Planner.Chat;

public sealed class ChatEnvelope
{
    public int V { get; set; } = ChatRoutes.ProtocolVersion;
    public string Type { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public JsonElement Payload { get; set; }
}

public static class ChatJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public static class ChatCodec
{
    public static string Pack<T>(string type, T payload)
    {
        var envelope = new
        {
            v = ChatRoutes.ProtocolVersion,
            type,
            id = Guid.NewGuid(),
            payload
        };
        return JsonSerializer.Serialize(envelope, ChatJson.Options);
    }

    public static ChatEnvelope? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatEnvelope>(json, ChatJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public static T? Payload<T>(ChatEnvelope envelope)
    {
        try
        {
            return envelope.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? default
                : envelope.Payload.Deserialize<T>(ChatJson.Options);
        }
        catch
        {
            return default;
        }
    }

    public static Uri ToWebSocketUri(string httpBase, string token)
    {
        var trimmed = (httpBase ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed + ChatRoutes.WebSocket, UriKind.Absolute, out var http))
        {
            throw new InvalidOperationException("Sunucu adresi geçersiz.");
        }

        var builder = new UriBuilder(http)
        {
            Scheme = http.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Query = "token=" + Uri.EscapeDataString(token)
        };
        return builder.Uri;
    }
}
