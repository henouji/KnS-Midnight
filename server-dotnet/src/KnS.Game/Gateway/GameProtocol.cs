namespace KnS.Game.Gateway;

/// <summary>
/// Protocol numbers of the game gateway: the request's "protocol" field, and the case of
/// JanssonParser@0x15a298 in libgame.so that parses the reply. 140 and 141 have cases but no
/// handler yet; unhandled protocols get a bare errno=0, which the client accepts.
/// </summary>
public enum GameProtocol
{
    VersionCheck = 48,
    Login = 51,
    HubUid = 52,
    HubDevice = 53,
    EventInfo = 59,
    LoadUser = 60,
    Inventory = 61,
    StageInfo = 70,
    StageStart = 71,
    StageResult = 72,
    StageContinue = 73,
    AreaReward = 74,
    EquipItem = 80,
    PowerUpItem = 81,
    EnhanceItem = 82,
    SellItems = 83,
    LockItem = 84,
    FellowDraw = 92,
    ExpandItemBag = 106,
    HeroBuy = 201,
    HeroChange = 202,
    ExpandAllyBag = 301,
    FellowSet = 302,
    FellowPowerUp = 303,
    FellowSell = 306,
    FellowEnhance = 307,
}
