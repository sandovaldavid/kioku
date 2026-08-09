using System.Globalization;
using System.Text;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// First-class engineering specifications plus the additive spec-to-plan relationship.
/// All writes go through the same vault mutation boundary used by the existing engineering tools.
/// </summary>
public sealed class EngineeringSpecService(
    ProjectWorkspaceService workspace,
    VaultConfigService vaultConfig,
    VaultIndexService vault,
    ObsidianBridgeService bridge,
    IVaultMutationService mutations)
{
    private static readonly string[] SpecStatuses = ["draft", "approved", "superseded", "discarded"];
    private static readonly string[] PlanStatuses = ["draft", "active", "done"];

    public async Task<string> CreateSpecAsync(
        string project,
        string title,
        string objective,
        string requirements,
        string status = "draft",
        string sourceIssue = "",
        string tags = "",
        string context = "",
        string nonGoals = "",
        string architecture = "",
        string components = "",
        string dataFlow = "",
        string errorHandling = "",
        string securityPrivacy = "",
        string compatibility = "",
        string testingStrategy = "",
        string decisions = "",
        string openQuestions = "",
        string related = "",
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } projectError)
        {
            return projectError;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "[error] The 'title' parameter cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(objective))
        {
            return "[error] The 'objective' parameter cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(requirements))
        {
            return "[error] The 'requirements' parameter cannot be empty.";
        }

        if (!SpecStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            return $"[error] Invalid status '{status}' for a spec. Valid options: {string.Join(", ", SpecStatuses)}.";
        }

        var safeTitle = NoteHelpers.SanitizeFileName(title);
        if (string.IsNullOrWhiteSpace(safeTitle) || safeTitle is "." or "..")
        {
            return "[error] The title does not produce a safe spec filename.";
        }

        var scaffolded = await workspace.EnsureProjectScaffoldAsync(project).WaitAsync(cancellationToken);
        var folder = workspace.GetSubfolder(project, "specs");
        var fileName = $"SPEC-{DateTime.Now:yyyy-MM-dd}-{safeTitle}";
        var filePath = NoteHelpers.EnsureInsideVault(
            workspace.ProjectsRoot,
            Path.Combine(folder, fileName + ".md"));

        if (File.Exists(filePath) && string.IsNullOrWhiteSpace(preconditions?.MutationId))
        {
            return $"[error] Note already exists: '{workspace.ToVaultRelative(filePath)}'. Use edit_note to modify it.";
        }

        var projectLink = $"[[{ProjectWorkspaceService.ProjectLeafName(project)}]]";
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["project"] = project,
            ["project_link"] = projectLink,
            ["objective"] = objective,
            ["context"] = context,
            ["requirements"] = requirements,
            ["non_goals"] = nonGoals,
            ["architecture"] = architecture,
            ["components"] = components,
            ["data_flow"] = dataFlow,
            ["error_handling"] = errorHandling,
            ["security_privacy"] = securityPrivacy,
            ["compatibility"] = compatibility,
            ["testing_strategy"] = testingStrategy,
            ["decisions"] = decisions,
            ["open_questions"] = openQuestions,
            ["related"] = related,
            ["source_issue"] = sourceIssue,
        };

        var body = NoteHelpers.ExpandTemplateVariables(
            await workspace.ResolveTemplateAsync("spec").WaitAsync(cancellationToken),
            variables,
            noteTitle: title);

        var relFolder = workspace.ToVaultRelative(folder);
        var mergedTags = NoteHelpers.MergeTagsWithInheritance(
            NoteHelpers.ParseTags(tags).Prepend("spec"),
            vaultConfig.GetInheritedTags(relFolder),
            vaultConfig.ExcludeFromTags);
        var extraFields = new Dictionary<string, string>
        {
            ["project"] = project,
            ["project_link"] = $"\"{projectLink}\"",
        };
        if (!string.IsNullOrWhiteSpace(sourceIssue))
        {
            extraFields["source_issue"] = sourceIssue.Trim();
        }

        var content = NoteHelpers.BuildFrontmatter(
            mergedTags,
            type: "spec",
            status: status.ToLowerInvariant(),
            date: DateOnly.FromDateTime(DateTime.Now),
            domain: vaultConfig.GetDomainForFolder(relFolder),
            cssClasses: ["kioku-spec"],
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null,
            extraFields: extraFields) + "\n" + body;

        var receipt = await mutations.CreateTextAsync(filePath, content, preconditions, cancellationToken);
        await vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);

        var relativePath = workspace.ToVaultRelative(filePath);
        var (finalRevision, evalResult) = await FinalizeCreatedArtifactAsync(
            filePath, body, receipt, cancellationToken);

        var sb = new StringBuilder($"[ok] Spec created: {relativePath}");
        if (!string.IsNullOrWhiteSpace(finalRevision))
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n   revision: {finalRevision}");
        }
        if (scaffolded.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n   Scaffolded project '{project}' ({scaffolded.Count} new folder(s)/note(s)).");
        }
        if (evalResult.Warning is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n   [warning] {evalResult.Warning}");
        }

        return sb.ToString();
    }

    public async Task<string> CreatePlanFromSpecAsync(
        string project,
        string title,
        string objective,
        string steps,
        string spec,
        string status = "draft",
        string ticket = "",
        string tags = "",
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } projectError)
        {
            return projectError;
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            return "[error] The 'title' parameter cannot be empty.";
        }
        if (string.IsNullOrWhiteSpace(objective))
        {
            return "[error] The 'objective' parameter cannot be empty.";
        }
        if (string.IsNullOrWhiteSpace(steps))
        {
            return "[error] The 'steps' parameter cannot be empty.";
        }
        if (!PlanStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            return $"[error] Invalid status '{status}' for a plan. Valid options: {string.Join(", ", PlanStatuses)}.";
        }

        var resolved = await ResolveSpecAsync(project, spec, cancellationToken);
        if (!resolved.Success)
        {
            return resolved.Error!;
        }

        var specStatus = resolved.Status!;
        if (specStatus is "superseded" or "discarded")
        {
            return $"[error] Spec '{resolved.Name}' has status '{specStatus}' and is historical/non-actionable. Reconcile with a current spec before planning.";
        }

        var safeTitle = NoteHelpers.SanitizeFileName(title);
        if (string.IsNullOrWhiteSpace(safeTitle) || safeTitle is "." or "..")
        {
            return "[error] The title does not produce a safe plan filename.";
        }

        var scaffolded = await workspace.EnsureProjectScaffoldAsync(project).WaitAsync(cancellationToken);
        var folder = workspace.GetSubfolder(project, "plans");
        var fileName = $"PLAN-{DateTime.Now:yyyy-MM-dd}-{safeTitle}";
        var filePath = NoteHelpers.EnsureInsideVault(
            workspace.ProjectsRoot,
            Path.Combine(folder, fileName + ".md"));
        if (File.Exists(filePath) && string.IsNullOrWhiteSpace(preconditions?.MutationId))
        {
            return $"[error] Note already exists: '{workspace.ToVaultRelative(filePath)}'. Use edit_note to modify it.";
        }

        var projectLink = $"[[{ProjectWorkspaceService.ProjectLeafName(project)}]]";
        var specLink = $"[[{resolved.Name}]]";
        var ticketLink = NormalizeOptionalWikiLink(ticket);
        var body = NoteHelpers.ExpandTemplateVariables(
            await workspace.ResolveTemplateAsync("plan").WaitAsync(cancellationToken),
            new Dictionary<string, string>
            {
                ["project"] = project,
                ["project_link"] = projectLink,
                ["objective"] = objective,
                ["steps"] = steps,
                ["ticket"] = ticketLink,
            },
            noteTitle: title);

        var relFolder = workspace.ToVaultRelative(folder);
        var mergedTags = NoteHelpers.MergeTagsWithInheritance(
            NoteHelpers.ParseTags(tags).Prepend("plan"),
            vaultConfig.GetInheritedTags(relFolder),
            vaultConfig.ExcludeFromTags);
        var extraFields = new Dictionary<string, string>
        {
            ["project"] = project,
            ["project_link"] = $"\"{projectLink}\"",
            ["spec"] = $"\"{specLink}\"",
        };
        if (!string.IsNullOrWhiteSpace(ticketLink))
        {
            extraFields["ticket"] = $"\"{ticketLink}\"";
        }

        var content = NoteHelpers.BuildFrontmatter(
            mergedTags,
            type: "plan",
            status: status.ToLowerInvariant(),
            date: DateOnly.FromDateTime(DateTime.Now),
            domain: vaultConfig.GetDomainForFolder(relFolder),
            cssClasses: ["kioku-plan"],
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null,
            extraFields: extraFields) + "\n" + body;

        var receipt = await mutations.CreateTextAsync(filePath, content, preconditions, cancellationToken);
        await vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);

        var relativePath = workspace.ToVaultRelative(filePath);
        var (finalRevision, evalResult) = await FinalizeCreatedArtifactAsync(
            filePath, body, receipt, cancellationToken);

        var sb = new StringBuilder($"[ok] Plan created: {relativePath}\n   spec: {specLink}");
        if (!string.IsNullOrWhiteSpace(finalRevision))
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n   revision: {finalRevision}");
        }
        if (specStatus == "draft")
        {
            sb.Append("\n   [warning] Linked spec is draft; implementation may proceed, but the requirements are not yet approved.");
        }
        if (scaffolded.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n   Scaffolded project '{project}' ({scaffolded.Count} new folder(s)/note(s)).");
        }
        if (evalResult.Warning is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n   [warning] {evalResult.Warning}");
        }

        return sb.ToString();
    }

    private async Task<(string Revision, TemplaterEvaluationResult Evaluation)> FinalizeCreatedArtifactAsync(
        string filePath,
        string renderedBody,
        VaultMutationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var evaluation = TemplaterEvaluationResult.NotNeeded;
        if (!receipt.AlreadyApplied)
        {
            evaluation = await bridge.EvaluateTemplaterInPlaceAsync(
                renderedBody, workspace.ToVaultRelative(filePath), cancellationToken);
            if (evaluation.Applied)
            {
                await vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);
            }
        }

        // Templater can change the file after the atomic mutation receipt is produced. Re-read the
        // durable file so the public revision is truthful. An idempotent retry skips Templater and
        // simply reports the current durable revision instead of replaying an external side effect.
        var finalContent = await NoteHelpers.ReadAllTextAsync(filePath, cancellationToken);
        return (VaultRevision.Compute(finalContent), evaluation);
    }

    public async Task<string> BuildSpecsSectionAsync(
        string project,
        bool includeContent,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } projectError)
        {
            return projectError;
        }
        if (!Directory.Exists(workspace.GetProjectFolder(project)))
        {
            return $"[error] Project '{project}' does not exist. Use list_projects to discover names.";
        }

        var entries = new List<SpecEntry>();
        foreach (var file in workspace.EnumerateProjectDocs(project, "specs"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await NoteHelpers.ReadAllTextAsync(file.FullName, cancellationToken);
            var metadata = FrontmatterParser.Parse(raw);
            var status = (metadata.Status ?? "draft").ToLowerInvariant();
            if (!string.Equals(metadata.NoteType, "spec", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new SpecEntry(file, raw, status));
        }

        var ordered = entries
            .OrderBy(entry => StatusRank(entry.Status))
            .ThenByDescending(entry => entry.File.LastWriteTimeUtc)
            .ThenBy(entry => entry.File.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(limit, 0))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Specs ({entries.Count})");
        sb.AppendLine("> Semantics: approved = current requirements; draft = in progress; superseded/discarded = historical and not current requirements.");
        sb.AppendLine();

        foreach (var entry in ordered)
        {
            var disposition = entry.Status switch
            {
                "approved" => "current",
                "draft" => "in-progress",
                _ => "historical",
            };
            var summary = FirstBodyLine(entry.Raw);
            sb.Append("- [").Append(entry.Status).Append('/').Append(disposition).Append("] ")
                .Append(Path.GetFileNameWithoutExtension(entry.File.Name))
                .Append(" — ").Append(workspace.ToVaultRelative(entry.File.FullName))
                .Append(" (").Append(entry.File.LastWriteTimeUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(')');
            if (!string.IsNullOrWhiteSpace(summary))
            {
                sb.Append(" — ").Append(summary);
            }
            sb.AppendLine();

            if (includeContent)
            {
                sb.AppendLine();
                sb.AppendLine(entry.Raw.TrimEnd());
                sb.AppendLine();
            }
        }

        if (ordered.Count == 0)
        {
            sb.AppendLine("_(none)_");
        }

        return sb.ToString().TrimEnd();
    }

    public async Task<string> BuildSpecsOnlyContextAsync(
        string project,
        bool includeContent,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } projectError)
        {
            return projectError;
        }

        var projectFolder = workspace.GetProjectFolder(project);
        if (!Directory.Exists(projectFolder))
        {
            return $"[error] Project '{project}' does not exist. Use list_projects to discover names.";
        }

        var leaf = Path.GetFileName(projectFolder);
        var mocPath = Path.Combine(projectFolder, leaf + ".md");
        var sb = new StringBuilder($"# Project: {project}\n");
        if (File.Exists(mocPath))
        {
            var moc = await NoteHelpers.ReadAllTextAsync(mocPath, cancellationToken);
            sb.AppendLine("\n## MOC\n");
            sb.AppendLine(moc.TrimEnd());
        }
        sb.AppendLine();
        sb.AppendLine(await BuildSpecsSectionAsync(project, includeContent, limit, cancellationToken));
        return sb.ToString().TrimEnd();
    }

    public static string InjectSpecsSection(string baseContext, string specsSection)
    {
        var marker = "\n## Plans";
        var index = baseContext.IndexOf(marker, StringComparison.Ordinal);
        return index >= 0
            ? baseContext.Insert(index, "\n" + specsSection.TrimEnd() + "\n")
            : baseContext.TrimEnd() + "\n\n" + specsSection.TrimEnd();
    }

    private async Task<ResolvedSpec> ResolveSpecAsync(
        string project,
        string reference,
        CancellationToken cancellationToken)
    {
        var candidate = NormalizeSpecReference(reference);
        if (candidate is null)
        {
            return InvalidSpecReference();
        }

        var localFiles = workspace.EnumerateProjectDocs(project, "specs").ToList();
        if (FindSpecFile(localFiles, candidate) is { } localFile)
        {
            return await ValidateResolvedSpecAsync(project, localFile, cancellationToken);
        }

        foreach (var otherProject in workspace.DiscoverProjects().Where(p => !string.Equals(p, project, StringComparison.OrdinalIgnoreCase)))
        {
            var otherFiles = workspace.EnumerateProjectDocs(otherProject, "specs").ToList();
            if (FindSpecFile(otherFiles, candidate) is { } otherFile)
            {
                var canonicalName = Path.GetFileNameWithoutExtension(otherFile.Name);
                return ResolvedSpec.Fail($"[error] Spec '{canonicalName}' belongs to project '{otherProject}', not '{project}'.");
            }
        }

        // A literal '#' is valid in a generated spec basename and is handled by the exact lookup
        // above. If no exact basename exists, treat '#' as unsupported heading syntax rather than
        // truncating or interpreting it.
        if (candidate.Contains('#'))
        {
            return InvalidSpecReference();
        }

        return ResolvedSpec.Fail($"[error] Spec '{candidate}' was not found in project '{project}'.");
    }

    private static async Task<ResolvedSpec> ValidateResolvedSpecAsync(
        string project,
        FileInfo file,
        CancellationToken cancellationToken)
    {
        var canonicalName = Path.GetFileNameWithoutExtension(file.Name);
        var raw = await NoteHelpers.ReadAllTextAsync(file.FullName, cancellationToken);
        var metadata = FrontmatterParser.Parse(raw);
        if (!string.Equals(metadata.NoteType, "spec", StringComparison.OrdinalIgnoreCase))
        {
            return ResolvedSpec.Fail($"[error] '{canonicalName}' exists in the specs folder but is not type: spec.");
        }

        if (metadata.ExtraFields.TryGetValue("project", out var declaredProject) &&
            !string.Equals(declaredProject, project, StringComparison.OrdinalIgnoreCase))
        {
            return ResolvedSpec.Fail($"[error] Spec '{canonicalName}' declares project '{declaredProject}', not '{project}'.");
        }

        var status = (metadata.Status ?? "draft").ToLowerInvariant();
        if (!SpecStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            return ResolvedSpec.Fail($"[error] Spec '{canonicalName}' has unsupported status '{status}'. Valid options: {string.Join(", ", SpecStatuses)}.");
        }

        return ResolvedSpec.Ok(canonicalName, status);
    }

    private static FileInfo? FindSpecFile(IReadOnlyList<FileInfo> files, string candidate)
    {
        // Canonical lookup is basename-first. This preserves every basename Kioku itself can
        // generate, including literal '#', dotted names, internal '..', and titles ending in .md.
        var exact = files.FirstOrDefault(file =>
            string.Equals(Path.GetFileNameWithoutExtension(file.Name), candidate, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Also accept a storage filename for callers that pass the final .md extension. This is
        // deliberately secondary so a real generated basename that itself ends in .md wins.
        if (candidate.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var withoutStorageExtension = candidate[..^3];
            return files.FirstOrDefault(file =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(file.Name),
                    withoutStorageExtension,
                    StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static ResolvedSpec InvalidSpecReference() =>
        ResolvedSpec.Fail("[error] Invalid spec reference. Use an exact spec basename such as 'SPEC-2026-08-09-first-class-specs' or '[[SPEC-...]]'; paths, aliases, headings, and traversal are not accepted.");

    private static string? NormalizeSpecReference(string reference)
    {
        var value = reference.Trim();
        if (value.StartsWith("[[", StringComparison.Ordinal) && value.EndsWith("]]", StringComparison.Ordinal))
        {
            value = value[2..^2].Trim();
        }
        else if (value.Contains("[[", StringComparison.Ordinal) || value.Contains("]]", StringComparison.Ordinal))
        {
            return null;
        }

        // Reject actual paths/aliases. Internal dots and literal '#' remain valid basename
        // characters and are resolved only by exact lookup within the project's specs folder.
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.Contains('/') || value.Contains('\\') || value.Contains('|'))
        {
            return null;
        }

        return value;
    }

    private static string NormalizeOptionalWikiLink(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }
        return trimmed.StartsWith("[[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal)
            ? trimmed
            : $"[[{trimmed}]]";
    }

    private static int StatusRank(string status) => status switch
    {
        "approved" => 0,
        "draft" => 1,
        "superseded" => 2,
        "discarded" => 3,
        _ => 4,
    };

    private static string FirstBodyLine(string raw)
    {
        var body = raw[FrontmatterParser.GetBodyStart(raw)..];
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#') && !trimmed.StartsWith('>') &&
                !trimmed.StartsWith("_(", StringComparison.Ordinal) && !trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
            }
        }
        return string.Empty;
    }

    private sealed record SpecEntry(FileInfo File, string Raw, string Status);

    private sealed record ResolvedSpec(bool Success, string? Name, string? Status, string? Error)
    {
        public static ResolvedSpec Ok(string name, string status) => new(true, name, status, null);
        public static ResolvedSpec Fail(string error) => new(false, null, null, error);
    }
}
