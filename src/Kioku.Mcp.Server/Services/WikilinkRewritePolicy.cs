using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Converts canonical wikilink resolution into a safe write-side replacement decision.
/// Resolution stays in <see cref="VaultLinkResolver"/>/<see cref="VaultIndexService"/>;
/// <see cref="WikilinkRewriter"/> remains a pure string transformer.
/// </summary>
public static class WikilinkRewritePolicy
{
    public static WikilinkRewriter.TargetRewriteDecision Decide(
        VaultIndexService vault,
        Note source,
        string rawTarget,
        WikilinkRewriter.RewritePlan plan)
    {
        var resolution = vault.ResolveLinkResult(source, rawTarget);
        if (resolution.Status == VaultLinkResolutionStatus.Resolved)
        {
            return DecideResolved(resolution, plan);
        }

        if (resolution.Status is VaultLinkResolutionStatus.Ambiguous or VaultLinkResolutionStatus.Malformed)
        {
            return IsPotentialHistoricalShortName(rawTarget, plan)
                ? WikilinkRewriter.TargetRewriteDecision.Ambiguous
                : WikilinkRewriter.TargetRewriteDecision.Leave;
        }

        // After the target has moved, its old spelling is intentionally Missing. Preserve the
        // historical rewrite behavior only for the exact old spelling/path (optionally followed
        // by a real fragment). A literal-hash filename that still exists resolves above and never
        // reaches this fallback.
        return DecideMissingHistoricalTarget(rawTarget, plan);
    }

    private static WikilinkRewriter.TargetRewriteDecision DecideResolved(
        VaultLinkResolution resolution,
        WikilinkRewriter.RewritePlan plan)
    {
        if (!CanonicalEquals(resolution.CanonicalTargetPath, plan.OldFullPath))
        {
            return WikilinkRewriter.TargetRewriteDecision.Leave;
        }

        var target = Normalize(resolution.Target);
        var fragment = resolution.Fragment ?? string.Empty;

        // Preserve the historical distinction between an explicit path and a bare name. Do not
        // canonicalize aliases or relative links merely because they resolve to the same note.
        if (target.Contains('/') && TargetEquals(target, plan.OldFullPath))
        {
            return WikilinkRewriter.TargetRewriteDecision.ReplaceWith(plan.NewFullPath + fragment);
        }

        if (!TargetEquals(target, plan.OldShortName))
        {
            return WikilinkRewriter.TargetRewriteDecision.Leave;
        }

        if (plan.ShortNameAmbiguous)
        {
            return WikilinkRewriter.TargetRewriteDecision.Ambiguous;
        }

        return plan.RewriteShortNameLinks
            ? WikilinkRewriter.TargetRewriteDecision.ReplaceWith(plan.NewShortName + fragment)
            : WikilinkRewriter.TargetRewriteDecision.Leave;
    }

    private static WikilinkRewriter.TargetRewriteDecision DecideMissingHistoricalTarget(
        string rawTarget,
        WikilinkRewriter.RewritePlan plan)
    {
        var normalized = Normalize(rawTarget);
        if (normalized.Length == 0)
        {
            return WikilinkRewriter.TargetRewriteDecision.Leave;
        }

        if (plan.OldFullPath.Contains('/') &&
            TryMatchTargetOrFragment(normalized, plan.OldFullPath, out var fullPathFragment))
        {
            return WikilinkRewriter.TargetRewriteDecision.ReplaceWith(plan.NewFullPath + fullPathFragment);
        }

        if (!TryMatchTargetOrFragment(normalized, plan.OldShortName, out var shortNameFragment))
        {
            return WikilinkRewriter.TargetRewriteDecision.Leave;
        }

        if (plan.ShortNameAmbiguous)
        {
            return WikilinkRewriter.TargetRewriteDecision.Ambiguous;
        }

        return plan.RewriteShortNameLinks
            ? WikilinkRewriter.TargetRewriteDecision.ReplaceWith(plan.NewShortName + shortNameFragment)
            : WikilinkRewriter.TargetRewriteDecision.Leave;
    }

    private static bool IsPotentialHistoricalShortName(
        string rawTarget,
        WikilinkRewriter.RewritePlan plan)
    {
        var normalized = Normalize(rawTarget);
        return TryMatchTargetOrFragment(normalized, plan.OldShortName, out _);
    }

    private static bool TryMatchTargetOrFragment(string rawTarget, string expectedTarget, out string fragment)
    {
        var expected = Normalize(expectedTarget);
        if (rawTarget.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            fragment = string.Empty;
            return true;
        }

        if (rawTarget.Length > expected.Length &&
            rawTarget.StartsWith(expected, StringComparison.OrdinalIgnoreCase) &&
            rawTarget[expected.Length] == '#')
        {
            fragment = rawTarget[expected.Length..];
            return true;
        }

        fragment = string.Empty;
        return false;
    }

    private static bool CanonicalEquals(string? actual, string expected) =>
        actual is not null && TargetEquals(actual, expected);

    private static bool TargetEquals(string left, string right) =>
        Normalize(left).Equals(Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string target)
    {
        var normalized = target.Trim().Replace('\\', '/').TrimStart('/');
        return normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^3]
            : normalized;
    }
}
