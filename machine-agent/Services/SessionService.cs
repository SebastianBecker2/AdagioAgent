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
    private readonly int _idleTimeoutSeconds;

    public SessionService(IOptions<global::AgentOptions>? options = null)
    {
        _maxConcurrentSessions = options?.Value.MaxConcurrentSessions ?? 5;
        _idleTimeoutSeconds = options?.Value.SessionIdleTimeoutSeconds ?? 3600;
        EnsureLegacyDefaultSession();
    }

    /// <summary>
    /// Create a new non-legacy session. Throws <see cref=""InvalidOperationException""/> when
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

    /// <summary>
    /// Evict non-legacy sessions idle longer than <c>SessionIdleTimeoutSeconds</c>.
    /// Returns the IDs of removed sessions.
    /// </summary>
    public IReadOnlyList<string> PruneExpiredSessions()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_idleTimeoutSeconds);
        var expired = new List<string>();

        foreach (var pair in _sessions)
        {
            if (!pair.Value.IsLegacyDefault && pair.Value.LastSeenAtUtc < cutoff)
            {
                if (_sessions.TryRemove(pair.Key, out _))
                {
                    expired.Add(pair.Key);
                }
            }
        }

        return expired;
    }

    /// <summary>Number of tracked sessions (includes the legacy default session).</summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>
    /// Age in seconds of the oldest non-legacy session, or <c>null</c> when there are none.
    /// </summary>
    public double? OldestNonLegacySessionAgeSeconds
    {
        get
        {
            var oldest = _sessions.Values
                .Where(s => !s.IsLegacyDefault)
                .OrderBy(s => s.CreatedAtUtc)
                .FirstOrDefault();
            return oldest is null
                ? null
                : (DateTimeOffset.UtcNow - oldest.CreatedAtUtc).TotalSeconds;
        }
    }
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
