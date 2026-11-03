namespace KnS.Data.Dialects;

/// <summary>
/// The SQL that differs between SQLite and MySQL. Statements are written once and pick the
/// dialect's spelling for column types and conflict handling.
/// </summary>
public abstract class SqlDialect
{
    /// <summary>An auto-numbered integer primary key column type.</summary>
    public abstract string AutoIncrementPrimaryKey { get; }

    public abstract string IntegerColumn { get; }

    public abstract string RealColumn { get; }

    public virtual string TextColumn => "TEXT";

    /// <summary>A text column that is part of a key or UNIQUE (MySQL cannot index TEXT).</summary>
    public abstract string KeyTextColumn { get; }

    /// <summary>Insert that skips rows whose key already exists.</summary>
    public abstract string InsertOrIgnore { get; }

    /// <summary>Insert that overwrites the row whose key already exists.</summary>
    public abstract string InsertOrReplace { get; }

    /// <summary>The query returning the id of the row the session inserted last.</summary>
    public abstract string LastInsertIdQuery { get; }

    /// <summary>
    /// The clause after an INSERT that turns a key conflict into an update; followed by the
    /// <c>column=value</c> assignments.
    /// </summary>
    public abstract string OnConflictUpdate(string conflictColumns);

    /// <summary>The isolation level of a session's transaction.</summary>
    public abstract System.Data.IsolationLevel TransactionIsolation { get; }

    /// <summary>The query listing a table's column names in order (parameter <c>@table</c>).</summary>
    public abstract string ColumnNamesQuery { get; }

    /// <summary>
    /// True for an error that only means another transaction got in the way (deadlock, lock wait
    /// timeout, busy database): the whole transaction can be rolled back and run again.
    /// </summary>
    public abstract bool IsTransientConflict(Exception exception);
}
