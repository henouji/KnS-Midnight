using System.Text.Json.Serialization;
using KnS.Game.Gateway;
using KnS.Game.Players;

namespace KnS.Game.Protocols.Session;

/// <summary>Protocol 51: game-server login (case @0x15f9a2). Issues the user number.</summary>
public sealed class LoginHandler(PlayerDirectory directory, GameOptions options) : IProtocolHandler
{
    public ProtocolResult Handle(GatewayRequest request)
    {
        Player player = directory.Resolve(request.Parameters);
        int audio = options.MuteAudio ? 0 : 1;
        return ProtocolResult.Success(new LoginResponse
        {
            UserNumber = player.UserNumber,
            SessionKey = "sk" + player.UserNumber,
            MusicOn = audio,
            SoundEffectsOn = audio,
        });
    }
}

public sealed record LoginResponse
{
    [JsonPropertyName("user_no")] public long UserNumber { get; init; }
    [JsonPropertyName("session_key")] public string SessionKey { get; init; } = "";

    /// <summary>1 raises the "played on another device" prompt.</summary>
    [JsonPropertyName("device_change")] public int DeviceChanged { get; init; }

    [JsonPropertyName("push_on")] public int PushNotificationsOn { get; init; } = 1;
    [JsonPropertyName("bgm_on")] public int MusicOn { get; init; } = 1;
    [JsonPropertyName("sound_effect_on")] public int SoundEffectsOn { get; init; } = 1;

    /// <summary>0 leaves the Mercury (CMS notice) fetch off.</summary>
    [JsonPropertyName("mercury")] public int MercuryOn { get; init; }

    /// <summary>0 leaves the request signing (not cracked) off.</summary>
    [JsonPropertyName("sha1_key")] public int RequestSigningOn { get; init; }
}
