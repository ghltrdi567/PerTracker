using System.Diagnostics;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Conditional [Fact] that skips the test if Docker is not running.
/// Used instead of [Fact] in integration tests that require Testcontainers.
///
/// When Docker is available: behaves like [Fact].
/// When Docker is NOT available: test is skipped (not failed).
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(CheckDockerAvailability);

    public override string? Skip =>
        DockerAvailable.Value ? null
        : "Docker Desktop is not running. Start Docker or use: dotnet test --filter \"FullyQualifiedName!~Integration\"";

    /// <summary>
    /// Checks Docker availability by running "docker info" and checking exit code.
    /// </summary>
    private static bool CheckDockerAvailability()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
