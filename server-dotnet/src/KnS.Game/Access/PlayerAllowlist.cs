using System.Globalization;
using KnS.Data.Repositories;
using KnS.Game.Gateway;

namespace KnS.Game.Access;

/// <summary>
/// Restricts the game gateway to testers (KNS_TESTERS_ONLY): a request passes when its device,
/// or the device of the user number it names, is attached to a tester who is not revoked.
/// </summary>
public sealed class PlayerAllowlist(GameOptions options, TesterRepository testers, PlayerRepository players, TimeProvider clock)
{
    private static readonly string[] DeviceParameters = ["hub_did", "did", "guest"];

    /// <summary>
    /// Sent before the login and carrying no player: the version check (48) and the patch list (50).
    /// Both answer the same for everyone; refusing them stops the client before it can log in.
    /// </summary>
    private const int PatchListProtocol = 50;

    public bool IsEnabled => options.TestersOnly;

    /// <summary>
    /// Whether the request comes from an active tester. Only the pre-login calls (48, 50) need no identity;
    /// any other request that names nobody is refused, since handlers fall back to the newest or
    /// last-loaded player when the request does not say. A refusal is recorded for the owner.
    /// </summary>
    public bool Permits(int protocol, GatewayRequest request)
    {
        if (!options.TestersOnly || protocol is (int)GameProtocol.VersionCheck or PatchListProtocol)
        {
            return true;
        }

        if (options.OpenAccess)
        {
            return PermitsUnlessRevoked(request);
        }

        string deviceId = "";
        foreach (string name in DeviceParameters)
        {
            if (request.Parameters.IsSet(name))
            {
                deviceId = request.Parameters.GetText(name, "");
                if (testers.IsActiveDevice(deviceId) || testers.IsLinkedDeviceAllowed(deviceId))
                {
                    return true;
                }

                break;
            }
        }

        long userNumber = request.Parameters.GetInt64OrFallback("user_no");
        if (userNumber == 0)
        {
            userNumber = request.HeaderUserNumber;
        }

        if (userNumber != 0 && testers.HasAllowedLink(userNumber))
        {
            return true;
        }

        if (userNumber != 0 && players.FindDeviceId(userNumber) is { } playerDevice)
        {
            if (testers.IsActiveDevice(playerDevice))
            {
                return true;
            }

            deviceId = deviceId.Length > 0 ? deviceId : playerDevice;
        }

        if (deviceId.Length > 0)
        {
            testers.RecordRefusal(deviceId, userNumber, clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0);
        }

        return false;
    }

    /// <summary>Open access: only a device (or the device of the named player) attached to a revoked tester is turned away.</summary>
    private bool PermitsUnlessRevoked(GatewayRequest request)
    {
        foreach (string name in DeviceParameters)
        {
            if (request.Parameters.IsSet(name) && testers.IsRevokedDevice(request.Parameters.GetText(name, "")))
            {
                return false;
            }
        }

        long userNumber = request.Parameters.GetInt64OrFallback("user_no");
        if (userNumber == 0)
        {
            userNumber = request.HeaderUserNumber;
        }

        return userNumber == 0 || players.FindDeviceId(userNumber) is not { } device || !testers.IsRevokedDevice(device);
    }

    /// <summary>The ids a refusal logs.</summary>
    public static string Describe(GatewayRequest request) =>
        "device=" + DeviceParameters.Where(request.Parameters.IsSet).Select(name => request.Parameters.GetText(name, "")).FirstOrDefault("-")
        + " user_no=" + (request.Parameters.GetInt64OrFallback("user_no") is var n and not 0 ? n : request.HeaderUserNumber).ToString(CultureInfo.InvariantCulture);
}
