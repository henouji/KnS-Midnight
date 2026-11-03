using KnS.Data;

namespace KnS.Server.Admin;

public enum AdminOutcome
{
    Ok,
    Invalid,
    NotFound,
    Conflict,
}

/// <summary>What an admin action did; the endpoint turns it into an HTTP status.</summary>
public readonly record struct AdminResult(AdminOutcome Outcome, string Message = "")
{
    public static AdminResult Ok { get; } = new(AdminOutcome.Ok);

    public static AdminResult Invalid(string message) => new(AdminOutcome.Invalid, message);

    public static AdminResult NotFound(string message) => new(AdminOutcome.NotFound, message);

    public static AdminResult Conflict(string message) => new(AdminOutcome.Conflict, message);
}

/// <summary>Runs one admin action in a single transaction: committed when it succeeds, rolled back when it fails or throws.</summary>
public sealed class AdminWork(DataSession session)
{
    public AdminResult Run(Func<AdminResult> action)
    {
        session.BeginTransaction();
        try
        {
            AdminResult result = action();
            if (result.Outcome == AdminOutcome.Ok)
            {
                session.Commit();
            }
            else
            {
                session.Rollback();
            }

            return result;
        }
        catch
        {
            session.Rollback();
            throw;
        }
    }
}
