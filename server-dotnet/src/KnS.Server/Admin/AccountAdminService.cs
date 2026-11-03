using KnS.Data.Repositories;

namespace KnS.Server.Admin;

/// <summary>Support actions on accounts: letting another device play an existing one.</summary>
public sealed class AccountAdminService(AccountAdminRepository accounts, AdminWork work, TimeProvider clock)
{
    private const int MaxDeviceIdLength = 200;
    private const long MaxCurrency = 999_999_999;

    public IReadOnlyList<AccountSummaryRecord> ListAccounts() => accounts.ListAccounts();

    public IReadOnlyList<DeviceLinkRecord> ListLinks() => accounts.ListLinks();

    public AdminResult Link(string deviceId, long userNumber)
    {
        deviceId = deviceId.Trim();
        if (deviceId.Length is 0 or > MaxDeviceIdLength)
        {
            return AdminResult.Invalid("device_id is required");
        }

        return work.Run(() =>
        {
            if (!accounts.AccountExists(userNumber))
            {
                return AdminResult.NotFound("no such account");
            }

            accounts.LinkDevice(deviceId, userNumber, clock.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0);
            return AdminResult.Ok;
        });
    }

    /// <summary>Corrects an account's gold and gems to exact amounts.</summary>
    public AdminResult SetWallet(long userNumber, long gold, long gems)
    {
        if (gold is < 0 or > MaxCurrency || gems is < 0 or > MaxCurrency)
        {
            return AdminResult.Invalid($"gold and gems must be 0 to {MaxCurrency}");
        }

        return work.Run(() =>
        {
            if (!accounts.AccountExists(userNumber))
            {
                return AdminResult.NotFound("no such account");
            }

            accounts.SetWallet(userNumber, gold, gems);
            return AdminResult.Ok;
        });
    }

    public AdminResult Unlink(string deviceId)
    {
        deviceId = deviceId.Trim();
        if (deviceId.Length is 0 or > MaxDeviceIdLength)
        {
            return AdminResult.Invalid("device_id is required");
        }

        return work.Run(() =>
        {
            accounts.UnlinkDevice(deviceId);
            return AdminResult.Ok;
        });
    }
}
