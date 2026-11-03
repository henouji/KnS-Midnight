namespace KnS.Data.Dialects;

public sealed class MySqlDialect : SqlDialect
{
    public static readonly MySqlDialect Instance = new();

    private MySqlDialect()
    {
    }

    public override string AutoIncrementPrimaryKey => "BIGINT AUTO_INCREMENT PRIMARY KEY";

    public override string IntegerColumn => "BIGINT";

    public override string RealColumn => "DOUBLE";

    public override string KeyTextColumn => "VARCHAR(191)";

    // The server default (REPEATABLE READ), as the Python server used.
    public override System.Data.IsolationLevel TransactionIsolation => System.Data.IsolationLevel.Unspecified;

    public override string InsertOrIgnore => "INSERT IGNORE";

    public override string InsertOrReplace => "REPLACE";

    public override string LastInsertIdQuery => "SELECT LAST_INSERT_ID()";

    public override string ColumnNamesQuery =>
        "SELECT COLUMN_NAME FROM information_schema.columns"
        + " WHERE table_schema = DATABASE() AND table_name = @table ORDER BY ORDINAL_POSITION";

    // MySQL detects the conflicting key itself.
    public override string OnConflictUpdate(string conflictColumns) => "ON DUPLICATE KEY UPDATE";

    public override bool IsTransientConflict(Exception exception) =>
        exception is MySqlConnector.MySqlException
        {
            ErrorCode: MySqlConnector.MySqlErrorCode.LockDeadlock or MySqlConnector.MySqlErrorCode.LockWaitTimeout,
        };
}
