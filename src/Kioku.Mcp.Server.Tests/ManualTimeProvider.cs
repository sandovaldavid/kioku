namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Deterministic clock used by workflow tests that need to advance UTC time explicitly.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}
