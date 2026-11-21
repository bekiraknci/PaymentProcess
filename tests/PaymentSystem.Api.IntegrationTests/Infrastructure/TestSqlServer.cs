using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace PaymentSystem.Api.IntegrationTests.Infrastructure;

internal static class TestSqlServer
{
    public const string EnvironmentVariable = "PAYMENTSYSTEM_TEST_SQLSERVER";
    private const string LocalDbInstance = "MSSQLLocalDB";

    private static readonly Lazy<string> Address = new(Resolve);

    public static string ConnectionString(string database) =>
        $"Server={Address.Value};Database={database};Trusted_Connection=True;TrustServerCertificate=True";

    private static string Resolve()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } configured)
        {
            return configured;
        }

        var localDb = $"(localdb)\\{LocalDbInstance}";
        if (CanConnect(localDb))
        {
            return localDb;
        }

        RunSqlLocalDb($"start {LocalDbInstance}");
        var pipe = RunSqlLocalDb($"info {LocalDbInstance}")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains(@"\pipe\", StringComparison.OrdinalIgnoreCase))?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        if (pipe is not null && CanConnect(pipe))
        {
            return pipe;
        }

        foreach (var runningPipe in RunningLocalDbPipes())
        {
            if (CanConnect(runningPipe))
            {
                return runningPipe;
            }
        }

        throw new InvalidOperationException(
            $"No SQL Server is reachable for integration tests. Install LocalDB or set {EnvironmentVariable} to a SQL Server address.");
    }

    private static IEnumerable<string> RunningLocalDbPipes()
    {
        string[] pipes;
        try
        {
            pipes = Directory.GetFiles(@"\\.\pipe\");
        }
        catch (IOException)
        {
            return [];
        }

        const string root = @"\\.\pipe\";
        return pipes
            .Select(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path[root.Length..] : path)
            .Where(name => name.StartsWith("LOCALDB#", StringComparison.OrdinalIgnoreCase) && name.EndsWith(@"\tsql\query", StringComparison.OrdinalIgnoreCase))
            .Select(name => $"np:{root}{name}")
            .Distinct();
    }

    private static bool CanConnect(string server)
    {
        try
        {
            using var connection = new SqlConnection($"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=10");
            connection.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static string RunSqlLocalDb(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sqllocaldb", arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return output;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }
}
