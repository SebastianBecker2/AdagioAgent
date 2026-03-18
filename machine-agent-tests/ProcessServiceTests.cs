using System.Diagnostics;
using AdagioMachineAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AdagioMachineAgent.Tests;

public sealed class ProcessServiceTests
{
    [Fact]
    public void Start_RejectsCommandOutsideWhitelist()
    {
        using var sut = CreateService(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetTempPath()],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });

        var command = ResolveLongRunningCommand().Command;

        var ex = Assert.Throws<InvalidOperationException>(() => sut.Start(command, null, null));

        Assert.Contains("not in an allowed executable path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_TracksProcess_AndGetReturnsTrackedInstance()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var sut = CreateService(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetDirectoryName(commandInfo.Command)!],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });

        TrackedProcess tracked = sut.Start(commandInfo.Command, commandInfo.Arguments, null);
        try
        {
            var fromGet = sut.Get(tracked.Process.Id);

            Assert.NotNull(fromGet);
            Assert.Equal(tracked.Process.Id, fromGet!.Process.Id);
            Assert.Equal("running", fromGet.Status);
        }
        finally
        {
            KillIfRunning(tracked.Process);
        }
    }

    [Fact]
    public void Start_EnforcesConcurrencyLimit()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var sut = CreateService(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetDirectoryName(commandInfo.Command)!],
            MaxConcurrentProcesses = 1,
            ProcessTimeoutSeconds = 60,
        });

        TrackedProcess first = sut.Start(commandInfo.Command, commandInfo.Arguments, null);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                sut.Start(commandInfo.Command, commandInfo.Arguments, null));

            Assert.Contains("Maximum concurrent process limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            KillIfRunning(first.Process);
        }
    }

    [Fact]
    public void TrackedProcessStatus_IsExitedAfterProcessEnds()
    {
        var commandInfo = ResolveQuickExitCommand();
        using var sut = CreateService(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetDirectoryName(commandInfo.Command)!],
            MaxConcurrentProcesses = 1,
            ProcessTimeoutSeconds = 60,
        });

        TrackedProcess tracked = sut.Start(commandInfo.Command, commandInfo.Arguments, null);
        tracked.Process.WaitForExit(5000);

        Assert.Equal("exited", tracked.Status);
    }

    [Fact]
    public void Start_RejectsPathPrefixBypassOutsideAllowedDirectory()
    {
        var allowedRoot = Path.Combine(Path.GetTempPath(), "allowed-root");
        Directory.CreateDirectory(allowedRoot);

        using var sut = CreateService(new global::AgentOptions
        {
            AllowedExecutablePaths = [allowedRoot],
            AllowedWritablePaths = [allowedRoot],
            AllowedReadablePaths = [allowedRoot],
            MaxConcurrentProcesses = 1,
            ProcessTimeoutSeconds = 60,
        });

        var bypassPath = Path.Combine(allowedRoot + "-evil", "installer.exe");

        var ex = Assert.Throws<InvalidOperationException>(() => sut.Start(bypassPath, null, null));
        Assert.Contains("not in an allowed executable path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerminateAllRunningProcesses_KillsRunningTrackedProcess()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var sut = CreateService(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetDirectoryName(commandInfo.Command)!],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });

        var tracked = sut.Start(commandInfo.Command, commandInfo.Arguments, null);

        var terminated = sut.TerminateAllRunningProcesses("test");

        tracked.Process.WaitForExit(3000);
        Assert.True(terminated >= 1);
        Assert.True(tracked.Process.HasExited);
    }

    [Fact]
    public void PruneExitedProcesses_RemovesExitedEntries()
    {
        var commandInfo = ResolveQuickExitCommand();
        using var sut = CreateService(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetDirectoryName(commandInfo.Command)!],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });

        var tracked = sut.Start(commandInfo.Command, commandInfo.Arguments, null);
        tracked.Process.WaitForExit(3000);

        var removed = sut.PruneExitedProcesses();

        Assert.True(removed >= 1);
        Assert.Null(sut.Get(tracked.Process.Id));
    }

    private static ProcessService CreateService(global::AgentOptions options)
    {
        return new ProcessService(
            Options.Create(options),
            NullLogger<ProcessService>.Instance);
    }

    private static (string Command, string Arguments) ResolveLongRunningCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            var command = Path.Combine(Environment.SystemDirectory, "ping.exe");
            return (command, "127.0.0.1 -n 20");
        }

        return ("/bin/sleep", "20");
    }

    private static (string Command, string? Arguments) ResolveQuickExitCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            var command = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return (command, "/c exit 0");
        }

        return ("/bin/true", null);
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
            // Best effort cleanup in tests.
        }
    }
}
