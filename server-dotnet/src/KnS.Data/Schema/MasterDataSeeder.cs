using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace KnS.Data.Schema;

/// <summary>
/// Creates a master table from its seed CSV's header and loads the rows while the table is empty.
/// </summary>
/// <remarks>
/// Master tables hold only seed data, so a table whose columns no longer match the CSV header
/// (the exporter gained columns) is dropped and reloaded. Columns are INTEGER when every value
/// in the CSV is a whole number, else text.
/// </remarks>
internal sealed partial class MasterDataSeeder(DataSession session, string masterDataDirectory, ILogger logger)
{
    public void Seed(MasterTable master)
    {
        string path = Path.Combine(masterDataDirectory, master.SeedFile);
        if (!File.Exists(path))
        {
            logger.LogWarning("Master data file {Path} is missing; {Table} is not loaded", path, master.Table);
            return;
        }

        List<string[]> rows = CsvFile.ReadAll(path);
        string[] columns = rows[0];
        List<string[]> values = rows.GetRange(1, rows.Count - 1);
        IReadOnlyList<string> keyColumns = master.KeyColumns ?? [columns[0]];

        IReadOnlyList<string> existingColumns = session.GetColumnNames(master.Table);
        if (existingColumns.Count > 0 && !existingColumns.SequenceEqual(columns))
        {
            logger.LogInformation("{Table} columns changed; reloading it from {File}", master.Table, master.SeedFile);
            session.Execute($"DROP TABLE {master.Table}");
        }

        session.Execute(CreateTableStatement(master.Table, columns, values, keyColumns));
        if (session.Exists($"SELECT 1 FROM {master.Table} LIMIT 1"))
        {
            return;
        }

        string placeholders = string.Join(",", columns.Select((_, i) => "@p" + i));
        session.BeginTransaction();
        session.ExecuteForEachRow(
            $"{session.Dialect.InsertOrIgnore} INTO {master.Table} VALUES({placeholders})",
            columns.Length,
            values.Select(row => row.Select(ToStoredValue).ToArray()));
        session.Commit();
    }

    private string CreateTableStatement(string table, string[] columns, List<string[]> rows, IReadOnlyList<string> keyColumns)
    {
        IEnumerable<string> definitions = columns.Select((column, i) =>
        {
            bool allWholeNumbers = rows.Where(row => i < row.Length).All(row => WholeNumber().IsMatch(row[i]));
            string type = allWholeNumbers ? session.Dialect.IntegerColumn
                : keyColumns.Contains(column) ? session.Dialect.KeyTextColumn
                : session.Dialect.TextColumn;
            return $"{column} {type}";
        });
        return $"CREATE TABLE IF NOT EXISTS {table}({string.Join(", ", definitions)}, PRIMARY KEY({string.Join(", ", keyColumns)}))";
    }

    private static object ToStoredValue(string value) =>
        WholeNumber().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number)
            ? number
            : value;

    [GeneratedRegex(@"^-?\d+$")]
    private static partial Regex WholeNumber();
}
