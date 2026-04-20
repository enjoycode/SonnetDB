using TSLite.Cli;
using Xunit;

namespace TSLite.Tests.Cli;

public sealed class CliApplicationTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"TSLite.Cli.Tests.{Guid.NewGuid():N}");

    public CliApplicationTests()
    {
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public void Run_WithoutArguments_PrintsHelp()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var app = new CliApplication(new StringReader(string.Empty), stdout, stderr);

        var exitCode = app.Run([]);

        Assert.Equal(0, exitCode);
        Assert.Contains("TSLite CLI 0.1.0", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Run_SqlCommand_WithEmbeddedConnection_PrintsResultTable()
    {
        var connectionString = $"Data Source={_rootDirectory}";

        var create = RunCommand(connectionString, "CREATE MEASUREMENT cpu (host TAG, value FIELD FLOAT)");
        Assert.Equal(0, create.ExitCode);
        Assert.Contains("OK", create.Stdout);

        var insert = RunCommand(
            connectionString,
            "INSERT INTO cpu(host, value, time) VALUES ('server-1', 63.2, 1776477601000)");
        Assert.Equal(0, insert.ExitCode);
        Assert.Contains("OK", insert.Stdout);

        var query = RunCommand(connectionString, "SELECT host, value FROM cpu");

        Assert.Equal(0, query.ExitCode);
        Assert.Contains("host", query.Stdout);
        Assert.Contains("value", query.Stdout);
        Assert.Contains("server-1", query.Stdout);
        Assert.Contains("63.2", query.Stdout);
        Assert.Contains("(1 row(s))", query.Stdout);
        Assert.Equal(string.Empty, query.Stderr);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static CommandResult RunCommand(string connectionString, string sql)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var app = new CliApplication(new StringReader(string.Empty), stdout, stderr);

        var exitCode = app.Run(
            ["sql", "--connection", connectionString, "--command", sql]);

        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private readonly record struct CommandResult(int ExitCode, string Stdout, string Stderr);
}
