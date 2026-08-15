using System.IO.Pipes;
using System.Text;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class InstallerShutdownServiceTests
{
    [TestMethod]
    public async Task SendsAcceptedBeforeCompletedAndExitsAfterTerminal()
    {
        var root = Path.Combine(
            @"D:\Development\Ligase\Build",
            "shutdown-service-" + Guid.NewGuid().ToString("N"));
        var service = new InstallerShutdownService(
            InstallationLayoutResolver.ResolveFromDesktopBase(root));
        var shutdownCalls = 0;
        var exitCalls = 0;
        service.Start(
            () =>
            {
                Interlocked.Increment(ref shutdownCalls);
                return Task.FromResult(new InstallerShutdownOutcome(
                    true, "exitCommitted", "completed", "stopped", false));
            },
            () => Interlocked.Increment(ref exitCalls));

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", service.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            using var reader = new StreamReader(
                pipe, new UTF8Encoding(false, true), false, 1024, true);
            await using var writer = new StreamWriter(
                pipe, new UTF8Encoding(false), 1024, true)
            { AutoFlush = true, NewLine = "\n" };
            const string requestId = "0123456789abcdef0123456789abcdef";
            await writer.WriteLineAsync(
                "{\"schemaVersion\":1,\"command\":\"shutdown\",\"requestId\":\"" +
                requestId + "\"}");

            Assert.AreEqual(
                "{\"schemaVersion\":1,\"requestId\":\"" + requestId +
                "\",\"state\":\"accepted\"}",
                await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(
                "{\"schemaVersion\":3,\"requestId\":\"" + requestId +
                "\",\"state\":\"completed\",\"code\":\"exitCommitted\",\"cleanupState\":\"completed\",\"coreStopCode\":\"stopped\",\"coreProcessStillAlive\":false,\"shutdownProtocolVersion\":3}",
                await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            await Task.Delay(50);
            Assert.AreEqual(1, shutdownCalls);
            Assert.AreEqual(1, exitCalls);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task CallbackExceptionProducesTypedFailureInsteadOfEof()
    {
        var root = Path.Combine(
            @"D:\Development\Ligase\Build",
            "shutdown-service-" + Guid.NewGuid().ToString("N"));
        var service = new InstallerShutdownService(
            InstallationLayoutResolver.ResolveFromDesktopBase(root));
        service.Start(
            () => throw new InvalidOperationException("fixtureFailure"),
            () => Assert.Fail("Failed shutdown must not exit the application."));

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", service.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            using var reader = new StreamReader(
                pipe, new UTF8Encoding(false, true), false, 1024, true);
            await using var writer = new StreamWriter(
                pipe, new UTF8Encoding(false), 1024, true)
            { AutoFlush = true, NewLine = "\n" };
            const string requestId = "fedcba9876543210fedcba9876543210";
            await writer.WriteLineAsync(
                "{\"schemaVersion\":1,\"command\":\"shutdown\",\"requestId\":\"" +
                requestId + "\"}");
            _ = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(
                "{\"schemaVersion\":3,\"requestId\":\"" + requestId +
                "\",\"state\":\"failed\",\"code\":\"exitCommitFailed\",\"cleanupState\":\"faulted\",\"coreStopCode\":\"notObserved\",\"coreProcessStillAlive\":true,\"shutdownProtocolVersion\":3}",
                await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ExitCommitIsNotReversedByDeferredCoreCleanup()
    {
        var root = Path.Combine(
            @"D:\Development\Ligase\Build",
            "shutdown-service-" + Guid.NewGuid().ToString("N"));
        var service = new InstallerShutdownService(
            InstallationLayoutResolver.ResolveFromDesktopBase(root));
        var exitCalls = 0;
        service.Start(
            () => Task.FromResult(new InstallerShutdownOutcome(
                true, "exitCommitted", "deferred", "gracefulTimeout", true)),
            () => Interlocked.Increment(ref exitCalls));

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", service.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            using var reader = new StreamReader(
                pipe, new UTF8Encoding(false, true), false, 1024, true);
            await using var writer = new StreamWriter(
                pipe, new UTF8Encoding(false), 1024, true)
            { AutoFlush = true, NewLine = "\n" };
            const string requestId = "00112233445566778899aabbccddeeff";
            await writer.WriteLineAsync(
                "{\"schemaVersion\":1,\"command\":\"shutdown\",\"requestId\":\"" +
                requestId + "\"}");
            _ = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            var terminal = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));

            StringAssert.Contains(terminal!, "\"state\":\"completed\"");
            StringAssert.Contains(terminal!, "\"cleanupState\":\"deferred\"");
            StringAssert.Contains(terminal!, "\"coreStopCode\":\"gracefulTimeout\"");
            await Task.Delay(50);
            Assert.AreEqual(1, exitCalls);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task RejectsMalformedRequestWithoutCallingShutdown()
    {
        var root = Path.Combine(
            @"D:\Development\Ligase\Build",
            "shutdown-service-" + Guid.NewGuid().ToString("N"));
        var service = new InstallerShutdownService(
            InstallationLayoutResolver.ResolveFromDesktopBase(root));
        var shutdownCalls = 0;
        service.Start(
            () =>
            {
                Interlocked.Increment(ref shutdownCalls);
                return Task.FromResult(new InstallerShutdownOutcome(
                    true, "exitCommitted", "completed", "stopped", false));
            },
            () => Assert.Fail("Exit callback must not run for a rejected request."));

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", service.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(3000);
            using var reader = new StreamReader(
                pipe, new UTF8Encoding(false, true), false, 1024, true);
            await using var writer = new StreamWriter(
                pipe, new UTF8Encoding(false), 1024, true)
            { AutoFlush = true, NewLine = "\n" };
            await writer.WriteLineAsync(
                "{\"schemaVersion\":1,\"command\":\"shutdown\",\"requestId\":true}");

            Assert.AreEqual(
                "{\"schemaVersion\":1,\"state\":\"rejected\",\"code\":\"requestInvalid\"}",
                await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(0, shutdownCalls);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }
}
