using System.Data.Common;
using Dapper;
using KnS.Data.Dialects;

namespace KnS.Data;

/// <summary>
/// One open database connection and its optional transaction: the unit of work of a request.
/// Every query of the session runs inside the transaction once it has begun. Disposing the
/// session rolls back anything not committed.
/// </summary>
public sealed class DataSession : IDisposable
{
    private readonly DbConnection _connection;
    private DbTransaction? _transaction;

    internal DataSession(DbConnection connection, SqlDialect dialect)
    {
        _connection = connection;
        Dialect = dialect;
    }

    public SqlDialect Dialect { get; }

    public bool InTransaction => _transaction is not null;

    /// <summary>Starts the transaction (no-op when one is already open).</summary>
    public void BeginTransaction()
    {
        _transaction ??= _connection.BeginTransaction(Dialect.TransactionIsolation);
    }

    public void Commit()
    {
        _transaction?.Commit();
        _transaction?.Dispose();
        _transaction = null;
    }

    public void Rollback()
    {
        _transaction?.Rollback();
        _transaction?.Dispose();
        _transaction = null;
    }

    public IReadOnlyList<T> Query<T>(string sql, object? parameters = null) =>
        _connection.Query<T>(sql, parameters, _transaction).AsList();

    /// <summary>The first row, or default when the query returns none.</summary>
    public T? QueryFirstOrDefault<T>(string sql, object? parameters = null) =>
        _connection.QueryFirstOrDefault<T>(sql, parameters, _transaction);

    /// <summary>Rows as column name → value, in column order (for tables read column by column).</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> QueryRows(string sql, object? parameters = null)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (object row in _connection.Query(sql, parameters, _transaction))
        {
            var columns = (IDictionary<string, object?>)row;
            rows.Add(columns.ToDictionary(pair => pair.Key, pair => pair.Value is DBNull ? null : pair.Value));
        }

        return rows;
    }

    public bool Exists(string sql, object? parameters = null) =>
        _connection.ExecuteScalar(sql, parameters, _transaction) is not null and not DBNull;

    /// <summary>Runs a statement and returns the number of rows it changed.</summary>
    public int Execute(string sql, object? parameters = null) =>
        _connection.Execute(sql, parameters, _transaction);

    /// <summary>Runs an INSERT and returns the new row's auto-numbered id.</summary>
    public long Insert(string sql, object? parameters = null)
    {
        _connection.Execute(sql, parameters, _transaction);
        return _connection.ExecuteScalar<long>(Dialect.LastInsertIdQuery, transaction: _transaction);
    }

    /// <summary>
    /// Runs one statement with positional parameters <c>@p0..@pN</c> for every row, prepared once
    /// (bulk loads of the master tables).
    /// </summary>
    public void ExecuteForEachRow(string sql, int parameterCount, IEnumerable<object?[]> rows)
    {
        using DbCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;
        for (int i = 0; i < parameterCount; i++)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i;
            command.Parameters.Add(parameter);
        }

        foreach (object?[] row in rows)
        {
            for (int i = 0; i < parameterCount; i++)
            {
                command.Parameters[i].Value = i < row.Length ? row[i] ?? DBNull.Value : DBNull.Value;
            }

            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<string> GetColumnNames(string table) =>
        Query<string>(Dialect.ColumnNamesQuery, new { table });

    public void Dispose()
    {
        Rollback();
        _connection.Dispose();
    }
}
