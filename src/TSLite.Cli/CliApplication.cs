using System.Globalization;
using System.Reflection;
using TSLite.Data;

namespace TSLite.Cli;

internal sealed class CliApplication(TextReader input, TextWriter output, TextWriter error)
{
    private readonly TextReader _input = input;
    private readonly TextWriter _output = output;
    private readonly TextWriter _error = error;

    public int Run(IReadOnlyList<string> args)
    {
        try
        {
            if (args.Count == 0)
            {
                WriteHelp();
                return ExitCodes.Success;
            }

            var command = args[0];
            if (IsHelp(command))
            {
                WriteHelp();
                return ExitCodes.Success;
            }

            if (IsVersion(command))
            {
                WriteVersion();
                return ExitCodes.Success;
            }

            return command.ToLowerInvariant() switch
            {
                "sql" => RunSql(args),
                "repl" => RunRepl(args),
                _ => FailParse($"未知命令 '{command}'。"),
            };
        }
        catch (CliUsageException ex)
        {
            _error.WriteLine(ex.Message);
            _error.WriteLine("使用 `tslite help` 查看帮助。");
            return ExitCodes.InvalidArguments;
        }
        catch (Exception ex)
        {
            _error.WriteLine(ex.Message);
            return ExitCodes.ExecutionFailed;
        }
    }

    private int RunSql(IReadOnlyList<string> args)
    {
        var options = ParseSqlOptions(args);
        ExecuteAndRender(options.ConnectionString, options.SqlText);
        return ExitCodes.Success;
    }

    private int RunRepl(IReadOnlyList<string> args)
    {
        var options = ParseReplOptions(args);

        _output.WriteLine("TSLite SQL REPL");
        _output.WriteLine($"连接: {options.ConnectionString}");
        _output.WriteLine("输入单行 SQL 后回车执行；输入 exit 或 quit 退出。");

        while (true)
        {
            _output.Write("tslite> ");
            _output.Flush();

            var line = _input.ReadLine();
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(line, "quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                ExecuteAndRender(options.ConnectionString, line);
            }
            catch (Exception ex)
            {
                _error.WriteLine(ex.Message);
            }
        }

        return ExitCodes.Success;
    }

    private void ExecuteAndRender(string connectionString, string sqlText)
    {
        using var connection = new TsdbConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sqlText;

        using var reader = command.ExecuteReader();
        if (reader.FieldCount == 0)
        {
            _output.WriteLine(FormattableString.Invariant($"OK ({reader.RecordsAffected} rows affected)"));
            return;
        }

        var headers = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            headers[i] = reader.GetName(i);
        }

        var rows = new List<string[]>(capacity: 32);
        while (reader.Read())
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = FormatValue(reader.GetValue(i));
            }

            rows.Add(row);
        }

        ConsoleTableFormatter.Write(_output, headers, rows);
        _output.WriteLine(FormattableString.Invariant($"({rows.Count} row(s))"));
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static SqlCommandOptions ParseSqlOptions(IReadOnlyList<string> args)
    {
        string? connectionString = null;
        string? sqlText = null;
        string? sqlFile = null;

        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--connection":
                case "-c":
                    connectionString = ReadRequiredValue(args, ref i, "连接字符串");
                    break;
                case "--command":
                case "-q":
                    sqlText = ReadRequiredValue(args, ref i, "SQL 文本");
                    break;
                case "--file":
                case "-f":
                    sqlFile = ReadRequiredValue(args, ref i, "SQL 文件路径");
                    break;
                case "--help":
                case "-h":
                    throw new CliUsageException(BuildSqlHelp());
                default:
                    throw new CliUsageException($"未知参数 '{args[i]}'。");
            }
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new CliUsageException("必须通过 --connection 指定连接字符串。");
        }

        if (string.IsNullOrWhiteSpace(sqlText) == string.IsNullOrWhiteSpace(sqlFile))
        {
            throw new CliUsageException("必须且只能通过 --command 或 --file 提供一条 SQL。");
        }

        return new SqlCommandOptions(
            connectionString,
            sqlText ?? File.ReadAllText(sqlFile!));
    }

    private static ReplCommandOptions ParseReplOptions(IReadOnlyList<string> args)
    {
        string? connectionString = null;

        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--connection":
                case "-c":
                    connectionString = ReadRequiredValue(args, ref i, "连接字符串");
                    break;
                case "--help":
                case "-h":
                    throw new CliUsageException(BuildReplHelp());
                default:
                    throw new CliUsageException($"未知参数 '{args[i]}'。");
            }
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new CliUsageException("必须通过 --connection 指定连接字符串。");
        }

        return new ReplCommandOptions(connectionString);
    }

    private static string ReadRequiredValue(IReadOnlyList<string> args, ref int index, string description)
    {
        if (index + 1 >= args.Count)
        {
            throw new CliUsageException($"参数 {args[index]} 缺少{description}。");
        }

        index++;
        return args[index];
    }

    private int FailParse(string message)
    {
        _error.WriteLine(message);
        _error.WriteLine("使用 `tslite help` 查看帮助。");
        return ExitCodes.InvalidArguments;
    }

    private void WriteHelp()
    {
        _output.WriteLine(
            """
TSLite CLI 0.1.0

用法:
  tslite version
  tslite sql  --connection "<connection-string>" (--command "<sql>" | --file ./query.sql)
  tslite repl --connection "<connection-string>"

示例:
  tslite sql --connection "Data Source=./demo-data" --command "CREATE MEASUREMENT cpu (host TAG, value FIELD FLOAT)"
  tslite sql --connection "Data Source=./demo-data" --command "SELECT count(*) FROM cpu"
  tslite sql --connection "Data Source=tslite+http://127.0.0.1:5080/metrics;Token=tslite-admin-token" --command "SHOW DATABASES"
  tslite repl --connection "Data Source=./demo-data"
""");
    }

    private void WriteVersion()
    {
        var version = typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(CliApplication).Assembly.GetName().Version?.ToString()
            ?? "0.1.0";

        _output.WriteLine($"TSLite CLI {version}");
    }

    private static bool IsHelp(string arg)
        => string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);

    private static bool IsVersion(string arg)
        => string.Equals(arg, "version", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "--version", StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, "-v", StringComparison.OrdinalIgnoreCase);

    private static string BuildSqlHelp()
        => "用法: tslite sql --connection \"<connection-string>\" (--command \"<sql>\" | --file ./query.sql)";

    private static string BuildReplHelp()
        => "用法: tslite repl --connection \"<connection-string>\"";
}

internal readonly record struct SqlCommandOptions(string ConnectionString, string SqlText);

internal readonly record struct ReplCommandOptions(string ConnectionString);

internal sealed class CliUsageException(string message) : Exception(message);

internal static class ExitCodes
{
    public const int Success = 0;
    public const int ExecutionFailed = 1;
    public const int InvalidArguments = 2;
}
