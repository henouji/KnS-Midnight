namespace KnS.Data.Dialects;

public sealed class SqliteDialect : SqlDialect
{
    public static readonly SqliteDialect Instance = new();

    private SqliteDialect()
    {
    }

    public override string AutoIncrementPrimaryKey => "INTEGER PRIMARY KEY AUTOINCREMENT";

    public override string IntegerColumn => "INTEGER";

    public override string RealColumn => "REAL";

    public override string KeyTextColumn => "TEXT";

    // Serializable makes Microsoft.Data.Sqlite open the transaction with BEGIN IMMEDIATE: the
    // write lock is taken up front (waiting out other writers) instead of failing on upgrade.
    public override System.Data.IsolationLevel TransactionIsolation => System.Data.IsolationLevel.Serializable;

    public override string InsertOrIgnore => "INSERT OR IGNORE";

    public override string InsertOrReplace => "INSERT OR REPLACE";

    public override string LastInsertIdQuery => "SELECT last_insert_rowid()";

    public override string ColumnNamesQuery => "SELECT name FROM pragma_table_info(@table) ORDER BY cid";

    public override string OnConflictUpdate(string conflictColumns) =>
        $"ON CONFLICT({conflictColumns}) DO UPDATE SET";

    // SQLITE_BUSY (5) / SQLITE_LOCKED (6): another writer held the file past the busy timeout.
    public override bool IsTransientConflict(Exception exception) =>
        exception is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 6 };
}
