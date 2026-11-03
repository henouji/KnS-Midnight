using KnS.Data.Repositories;
using KnS.Game.Rules;

namespace KnS.Game.Players;

/// <summary>A registered player.</summary>
/// <param name="HubUid">The Hub uid registered with ("-1" for Hive guests); may be null on old rows.</param>
/// <param name="Nickname">The nickname, or the default "Squire&lt;user number&gt;".</param>
public sealed record Player(long UserNumber, string? HubUid, string Nickname)
{
    public static Player From(PlayerRecord record) =>
        new(record.UserNumber, record.HubUid,
            string.IsNullOrEmpty(record.Nickname) ? PlayerRules.DefaultNickname(record.UserNumber) : record.Nickname);
}

/// <summary>
/// The player protocol 60 (or any request carrying a user_no) loaded last: the fallback for
/// requests that identify no player, such as a stage start sent without the USERNO header.
/// </summary>
public sealed class LastLoadedPlayer
{
    private readonly object _lock = new();
    private Player? _player;

    public Player? Current
    {
        get
        {
            lock (_lock)
            {
                return _player;
            }
        }
    }

    public void Set(Player player)
    {
        lock (_lock)
        {
            _player = player;
        }
    }
}
