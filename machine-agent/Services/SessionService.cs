using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Tracks active logical agent sessions so process state can be scoped per client.
/// A legacy default session is kept for backward-compatible callers that do not
/// yet send an explicit session header.
/// </summary>
public sealed class SessionService
{
    public const string SessionHeaderName = "X-Adagio-Session-ID";
    public const string LegacyDefaultSessionId = "legacy-default";

    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly int _maxConcurrentSessions;

    public SessionService(IOptions<global::AgentOptions>? options = null)
    {
        _maxConcurrentSessions = options?.Value.MaxConcurrentSessions ?? 5;
        EnsureLegacyDefaultSession();
    }

    /// <summary>
    /// Create a new non-legacy session. Throws <see cref="InvalidOperationException"/> when
    /// the configured <c>MaxConcurrentSessions</c> cap would be exceeded.
    /// </summary>
    public AgentSession Connect(string? clientName = null)
    {
        var nonLegacyCount = _sessions.Values.Count(s => !s.IsLegacyDefault);
        if (nonLegacyCount >= _maxConcurrentSessions)
        {
            throw new InvalidOperationException(
                $"Maximum concurrent session limit ({_maxConcurrentSessions}) has been reached. " +
                "Disconnect an existing session or increase MaxConcurrentSessions in appsettings.json.");
        }

        var session = new AgentSession(
            Guid.NewGuid().ToString("n"),
            DateTimeOffset.UtcNow,
            clientName,
            isLegacyDefault: false);
        _sessions[session.SessionId] = session;
        return session;
    }

    public AgentSession EnsureLegacyDefaultSession()
    {
        return _sessions.GetOrAdd(
            LegacyDefaultSessionId,
            _ => new AgentSession(LegacyDefaultSessionId, DateTimeOffset.UtcNow, null, isLegacyDefault: true));
    }

    public AgentSession? Resolve(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return EnsureLegacyDefaultSession();
        }

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Touch();
            return session;
        }

        return null;
    }

    public int ActiveSessionCount => _sessions.Count;
}

/// <summary>Metadata for an active logical agent session.</summary>
public sealed class AgentSession
{
    public AgentSession(string sessionId, DateTimeOffset createdAtUtc, string? clientName, bool isLegacyDefault)
    {
        SessionId = sessionId;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = createdAtUtc;
        ClientName = clientName;
        IsLegacyDefault = isLegacyDefault;
    }

    public string SessionId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public string? ClientName { get; }

    public bool IsLegacyDefault { get; }

    public void Touch()
    {
        LastSeenAtUtc = DateTimeOffset.UtcNow;
    }
}