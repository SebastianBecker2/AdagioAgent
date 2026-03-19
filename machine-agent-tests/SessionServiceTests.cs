using AdagioMachineAgent.Services;
using Microsoft.Extensions.Options;

namespace AdagioMachineAgent.Tests;

public sealed class SessionServiceTests
{
    [Fact]
    public void Connect_CreatesSessionAndResolvesById()
    {
        var sut = new SessionService();

        var session = sut.Connect("test-client");

        Assert.False(string.IsNullOrWhiteSpace(session.SessionId));
        Assert.Equal("test-client", session.ClientName);
        Assert.False(session.IsLegacyDefault);

        var resolved = sut.Resolve(session.SessionId);
        Assert.NotNull(resolved);
        Assert.Equal(session.SessionId, resolved!.SessionId);
    }

    [Fact]
    public void Resolve_WithNullOrEmpty_ReturnsLegacyDefaultSession()
    {
        var sut = new SessionService();

        var nullSession = sut.Resolve(null);
        var emptySession = sut.Resolve("");
        var whitespaceSession = sut.Resolve("   ");

        Assert.NotNull(nullSession);
        Assert.Equal(SessionService.LegacyDefaultSessionId, nullSession!.SessionId);
        Assert.True(nullSession.IsLegacyDefault);
        Assert.NotNull(emptySession);
        Assert.NotNull(whitespaceSession);
    }

    [Fact]
    public void Resolve_WithUnknownId_ReturnsNull()
    {
        var sut = new SessionService();

        var result = sut.Resolve("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public void Connect_EnforcesMaxConcurrentSessionsCap()
    {
        var options = Options.Create(new global::AgentOptions { MaxConcurrentSessions = 2 });
        var sut = new SessionService(options);

        sut.Connect("client-1");
        sut.Connect("client-2");

        var ex = Assert.Throws<InvalidOperationException>(() => sut.Connect("client-3"));
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void PruneExpiredSessions_RemovesOnlyIdleSessions()
    {
        // Set a 1-second idle timeout so we can expire sessions in the test.
        var options = Options.Create(new global::AgentOptions { SessionIdleTimeoutSeconds = 1 });
        var sut = new SessionService(options);

        var active = sut.Connect("active");
        var idle = sut.Connect("idle");

        // Advance idle session's last-seen time into the past by not touching it and waiting.
        Thread.Sleep(1100); // 100 ms margin over the 1 s timeout

        // Touch the "active" session so it is not pruned.
        sut.Resolve(active.SessionId);

        var removed = sut.PruneExpiredSessions();

        Assert.Contains(idle.SessionId, removed);
        Assert.DoesNotContain(active.SessionId, removed);
        Assert.Null(sut.Resolve(idle.SessionId));
        Assert.NotNull(sut.Resolve(active.SessionId));
    }

    [Fact]
    public void PruneExpiredSessions_NeverRemovesLegacyDefaultSession()
    {
        var options = Options.Create(new global::AgentOptions { SessionIdleTimeoutSeconds = 0 });
        var sut = new SessionService(options);

        var removed = sut.PruneExpiredSessions();

        Assert.DoesNotContain(SessionService.LegacyDefaultSessionId, removed);
        Assert.NotNull(sut.Resolve(null));
    }

    [Fact]
    public void OldestNonLegacySessionAgeSeconds_IsNullWhenNoNonLegacySessions()
    {
        var sut = new SessionService();

        Assert.Null(sut.OldestNonLegacySessionAgeSeconds);
    }

    [Fact]
    public void OldestNonLegacySessionAgeSeconds_IsNonNegativeAfterConnect()
    {
        var sut = new SessionService();

        sut.Connect("client");

        Assert.NotNull(sut.OldestNonLegacySessionAgeSeconds);
        Assert.True(sut.OldestNonLegacySessionAgeSeconds >= 0);
    }

    [Fact]
    public void ActiveSessionCount_IncludesLegacyDefault()
    {
        var sut = new SessionService();

        Assert.Equal(1, sut.ActiveSessionCount); // legacy default only

        sut.Connect("client");

        Assert.Equal(2, sut.ActiveSessionCount);
    }
}
