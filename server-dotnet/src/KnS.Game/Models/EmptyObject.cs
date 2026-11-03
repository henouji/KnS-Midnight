namespace KnS.Game.Models;

/// <summary>Serializes as <c>{}</c>: the client's placeholder for an empty slot or a missing record.</summary>
public sealed record EmptyObject
{
    public static readonly EmptyObject Instance = new();
}
