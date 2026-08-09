using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class EngineeringSpecServiceTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Scaffold_CreatesSpecsEagerly_ButKeepsDailyAndTicketsLazy()
    {
        var (_, workspace, _) = CreateService();

        await workspace.EnsureProjectScaffoldAsync("demo");

        Assert.Contains("specs", ProjectWorkspaceService.CoreSubfolderKeys);
        Assert.DoesNotContain("specs", ProjectWorkspaceService.OptionalSubfolderKeys);
        Assert.True(Directory.Exists(workspace.GetSubfolder("demo", "specs")));
        Assert.False(Directory.Exists(workspace.GetSubfolder("demo", "daily")));
        Assert.False(Directory.Exists(workspace.GetSubfolder("demo", "tickets")));
        Assert.True(File.Exists(workspace.GetVaultTemplatePath("spec")));
    }

    [Fact]
    public async Task Scaffold_GroupedProject_RespectsCustomSpecsFolder()
    {
        var configPath = Path.Combine(_fixture.VaultPath, ".kioku", "config.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(
            configPath,
            "engineering:\n  subfolders:\n    specs: designs\n    daily: journal\n    tickets: work-items\n",
            Encoding.UTF8);
        var (_, workspace, _) = CreateService();

        await workspace.EnsureProjectScaffoldAsync("Atena/api.core");

        Assert.EndsWith(Path.Combine("Atena", "api.core", "designs"), workspace.GetSubfolder("Atena/api.core", "specs"));
        Assert.True(Directory.Exists(workspace.GetSubfolder("Atena/api.core", "specs")));
        Assert.False(Directory.Exists(workspace.GetSubfolder("Atena/api.core", "daily")));
        Assert.False(Directory.Exists(workspace.GetSubfolder("Atena/api.core", "tickets")));
    }

    [Fact]
    public async Task CreateSpec_WritesFirstClassFrontmatterAndCanonicalSections()
    {
        var (service, workspace, _) = CreateService();

        var result = await service.CreateSpecAsync(
            "demo",
            "First class specs",
            "Define durable design artifacts.",
            "- Specs are distinct from plans.",
            status: "approved",
            sourceIssue: "#408",
            context: "External workflows need durable requirements.",
            nonGoals: "Do not make Kioku a workflow engine.",
            architecture: "Kioku remains the durable storage boundary.",
            securityPrivacy: "No direct vault filesystem writes from adapters.");

        Assert.StartsWith("[ok]", result);
        var path = Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md").Single();
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("type: spec", content);
        Assert.Contains("status: approved", content);
        Assert.Contains("project: demo", content);
        Assert.Contains("source_issue: '#408'", content.Replace("\"#408\"", "'#408'", StringComparison.Ordinal));
        Assert.Contains("## Objective", content);
        Assert.Contains("## Requirements", content);
        Assert.Contains("## Security / privacy", content);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("done")]
    [InlineData("banana")]
    public async Task CreateSpec_InvalidStatus_IsRejected(string status)
    {
        var (service, _, _) = CreateService();

        var result = await service.CreateSpecAsync("demo", "T", "objective", "requirements", status);

        Assert.StartsWith("[error]", result);
        Assert.Contains("draft, approved, superseded, discarded", result);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("group/../escape")]
    [InlineData("group\\escape")]
    public async Task CreateSpec_InvalidProject_DoesNotEscapeVault(string project)
    {
        var (service, _, _) = CreateService();

        var result = await service.CreateSpecAsync(project, "T", "objective", "requirements");

        Assert.StartsWith("[error]", result);
        Assert.False(Directory.Exists(Path.Combine(_fixture.VaultPath, "escape")));
    }

    [Fact]
    public async Task CreateSpec_ConflictAndIdempotentRetry_PreserveSingleFile()
    {
        var (service, workspace, _) = CreateService();
        var preconditions = VaultMutationPreconditions.FromToolArguments(mutationId: "spec-create-1");

        var first = await service.CreateSpecAsync(
            "demo", "Idempotent", "objective", "requirements", preconditions: preconditions);
        var retry = await service.CreateSpecAsync(
            "demo", "Idempotent", "objective", "requirements", preconditions: preconditions);
        var conflict = await service.CreateSpecAsync(
            "demo", "Idempotent", "objective", "requirements");

        Assert.StartsWith("[ok]", first);
        Assert.StartsWith("[ok]", retry);
        Assert.StartsWith("[error]", conflict);
        Assert.Single(Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md"));
    }

    [Fact]
    public async Task CreateSpec_ForwardsMutationPreconditions()
    {
        var (service, _, mutations) = CreateService();
        var preconditions = VaultMutationPreconditions.FromToolArguments(
            claimId: "claim-1",
            fenceGeneration: 7,
            resourceKey: "note:Projects/demo/specs/custom.md",
            mutationId: "spec-mutation-1");

        await service.CreateSpecAsync(
            "demo", "Preconditions", "objective", "requirements", preconditions: preconditions);

        Assert.NotNull(mutations.LastExplicitPreconditions);
        Assert.Equal("claim-1", mutations.LastExplicitPreconditions!.ClaimId);
        Assert.Equal(7, mutations.LastExplicitPreconditions.FenceGeneration);
        Assert.Equal("note:Projects/demo/specs/custom.md", mutations.LastExplicitPreconditions.ResourceKey);
        Assert.Equal("spec-mutation-1", mutations.LastExplicitPreconditions.MutationId);
    }

    [Fact]
    public async Task CreateSpec_StaleCreatePrecondition_IsRejectedByMutationBoundary()
    {
        var (service, _, _) = CreateService();
        var preconditions = VaultMutationPreconditions.FromToolArguments(expectedRevision: "STALE");

        var exception = await Assert.ThrowsAsync<VaultMutationException>(() =>
            service.CreateSpecAsync(
                "demo", "Stale", "objective", "requirements", preconditions: preconditions));

        Assert.Equal(VaultMutationErrorCodes.InvalidPrecondition, exception.Code);
    }

    [Fact]
    public async Task PlanWithApprovedSpec_AddsSpecFrontmatter_AndPlanWithoutSpecRemainsCompatible()
    {
        var (service, workspace, _) = CreateService();
        await service.CreateSpecAsync("demo", "Approved", "objective", "requirements", status: "approved");
        var specName = Path.GetFileNameWithoutExtension(
            Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md").Single());

        var linked = await service.CreatePlanFromSpecAsync(
            "demo", "Linked plan", "implement", "- [ ] step", specName, status: "draft");

        Assert.StartsWith("[ok]", linked);
        var plan = await File.ReadAllTextAsync(
            Directory.GetFiles(workspace.GetSubfolder("demo", "plans"), "PLAN-*.md").Single());
        Assert.Contains($"spec: \"[[{specName}]]\"", plan);

        var method = typeof(FocusedCreationTools).GetMethod(nameof(FocusedCreationTools.create_implementation_plan))!;
        var specParameter = method.GetParameters().Single(parameter => parameter.Name == "spec");
        Assert.True(specParameter.HasDefaultValue);
        Assert.Equal(string.Empty, specParameter.DefaultValue);
    }

    [Fact]
    public async Task PlanFromDraftSpec_IsAllowedWithWarning_ButHistoricalSpecsAreRejected()
    {
        var (service, workspace, _) = CreateService();
        await service.CreateSpecAsync("demo", "Draft", "objective", "requirements", status: "draft");
        await service.CreateSpecAsync("demo", "Old", "objective", "requirements", status: "superseded");
        var specs = Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md")
            .ToDictionary(path => Path.GetFileName(path).Contains("Draft", StringComparison.Ordinal) ? "draft" : "old");

        var draft = await service.CreatePlanFromSpecAsync(
            "demo", "Draft plan", "implement", "- [ ] step", Path.GetFileNameWithoutExtension(specs["draft"]));
        var old = await service.CreatePlanFromSpecAsync(
            "demo", "Old plan", "implement", "- [ ] step", Path.GetFileNameWithoutExtension(specs["old"]));

        Assert.StartsWith("[ok]", draft);
        Assert.Contains("[warning] Linked spec is draft", draft);
        Assert.StartsWith("[error]", old);
        Assert.Contains("historical/non-actionable", old);
    }

    [Fact]
    public async Task PlanSpecReference_RejectsMissingMalformedAndWrongProject()
    {
        var (service, workspace, _) = CreateService();
        await service.CreateSpecAsync("other", "Other", "objective", "requirements", status: "approved");
        var otherName = Path.GetFileNameWithoutExtension(
            Directory.GetFiles(workspace.GetSubfolder("other", "specs"), "SPEC-*.md").Single());

        var missing = await service.CreatePlanFromSpecAsync(
            "demo", "Missing", "objective", "- [ ] step", "SPEC-missing");
        var malformed = await service.CreatePlanFromSpecAsync(
            "demo", "Malformed", "objective", "- [ ] step", "../SPEC-bad");
        var mismatch = await service.CreatePlanFromSpecAsync(
            "demo", "Mismatch", "objective", "- [ ] step", otherName);

        Assert.Contains("was not found", missing);
        Assert.Contains("Invalid spec reference", malformed);
        Assert.Contains("belongs to project 'other'", mismatch);
    }

    [Fact]
    public async Task ProjectContext_AcceptsSpecAliases_AndMarksHistoricalSpecsClearly()
    {
        var (service, workspace, mutations) = CreateService();
        await service.CreateSpecAsync("demo", "Approved", "approved body", "requirements", status: "approved");
        await service.CreateSpecAsync("demo", "Draft", "draft body", "requirements", status: "draft");
        await service.CreateSpecAsync("demo", "Discarded", "discarded body", "requirements", status: "discarded");

        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var documents = new ProjectDocumentService(
            _fixture.Index,
            config,
            vaultConfig,
            workspace,
            bridge,
            new ProjectDocumentFileSystem());
        var tools = new EngineeringWorkflowTools(documents);

        var singular = await tools.get_project_context("demo", types: "spec", include_content: true);
        var plural = await tools.get_project_context("demo", types: "specs");
        var all = await tools.get_project_context("demo");

        Assert.Contains("## Specs (3)", singular);
        Assert.Contains("approved/current", singular);
        Assert.Contains("draft/in-progress", singular);
        Assert.Contains("discarded/historical", singular);
        Assert.Contains("discarded = historical", plural);
        Assert.Contains("## Specs (3)", all);
        Assert.True(singular.IndexOf("approved/current", StringComparison.Ordinal) < singular.IndexOf("draft/in-progress", StringComparison.Ordinal));
        Assert.True(singular.IndexOf("draft/in-progress", StringComparison.Ordinal) < singular.IndexOf("discarded/historical", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SpecTemplate_IsCanonical_AndExistingUserOverrideIsNotOverwritten()
    {
        var (_, workspace, _) = CreateService();

        await workspace.EnsureProjectScaffoldAsync("demo");
        var template = workspace.GetVaultTemplatePath("spec")!;
        Assert.Contains("## Requirements", await File.ReadAllTextAsync(template));
        await File.WriteAllTextAsync(template, "# custom spec template", Encoding.UTF8);

        await workspace.EnsureEngineeringTemplatesOnDiskAsync();

        Assert.Equal("# custom spec template", await File.ReadAllTextAsync(template));
    }

    private (EngineeringSpecService Service, ProjectWorkspaceService Workspace, TestMutationService Mutations) CreateService()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var mutations = new TestMutationService(_fixture.VaultPath);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge, mutations);
        var service = new EngineeringSpecService(workspace, vaultConfig, _fixture.Index, bridge, mutations);
        return (service, workspace, mutations);
    }

    private sealed class TestMutationService(string vaultPath) : IVaultMutationService
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (string Path, string Content, VaultMutationReceipt Receipt)> _mutations = [];

        public VaultMutationPreconditions? LastExplicitPreconditions { get; private set; }

        public Task<VaultMutationReceipt> CreateTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (preconditions is not null &&
                    (preconditions.HasContentPrecondition || preconditions.HasClaimPrecondition ||
                     !string.IsNullOrWhiteSpace(preconditions.MutationId) || !string.IsNullOrWhiteSpace(preconditions.ResourceKey)))
                {
                    LastExplicitPreconditions = preconditions;
                }

                if (!string.IsNullOrWhiteSpace(preconditions?.ExpectedRevision) ||
                    !string.IsNullOrWhiteSpace(preconditions?.ExpectedHash))
                {
                    throw new VaultMutationException(
                        VaultMutationErrorCodes.InvalidPrecondition,
                        "Create operations cannot satisfy a stale content precondition against an absent resource.");
                }

                if (!string.IsNullOrWhiteSpace(preconditions?.MutationId) &&
                    _mutations.TryGetValue(preconditions.MutationId, out var existing))
                {
                    if (existing.Path != path || existing.Content != content)
                    {
                        throw new VaultMutationException(VaultMutationErrorCodes.MutationIdReused, "Mutation id reused with different input.");
                    }

                    return Task.FromResult(existing.Receipt with { AlreadyApplied = true });
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream, NoteHelpers.Utf8NoBom);
                    writer.Write(content);
                }
                catch (IOException)
                {
                    throw new VaultMutationException(VaultMutationErrorCodes.WriteConflict, "Target already exists.");
                }

                var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
                var receipt = new VaultMutationReceipt($"note:{relative}", relative, VaultRevision.Compute(content));
                if (!string.IsNullOrWhiteSpace(preconditions?.MutationId))
                {
                    _mutations[preconditions.MutationId] = (path, content, receipt);
                }
                return Task.FromResult(receipt);
            }
        }

        public async Task<VaultMutationReceipt> WriteTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(path, content, NoteHelpers.Utf8NoBom, cancellationToken);
            var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
            return new VaultMutationReceipt($"note:{relative}", relative, VaultRevision.Compute(content));
        }

        public Task<VaultMutationReceipt> UpsertTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default) =>
            File.Exists(path)
                ? WriteTextAsync(path, content, preconditions, cancellationToken)
                : CreateTextAsync(path, content, preconditions, cancellationToken);

        public Task<VaultMutationReceipt> DeleteAsync(
            string path,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            File.Delete(path);
            var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
            return Task.FromResult(new VaultMutationReceipt($"note:{relative}", relative, null));
        }

        public async Task<VaultMutationReceipt> MoveAsync(
            string sourcePath,
            string destinationPath,
            string? replacementContent = null,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Move(sourcePath, destinationPath);
            if (replacementContent is not null)
            {
                await File.WriteAllTextAsync(destinationPath, replacementContent, NoteHelpers.Utf8NoBom, cancellationToken);
            }
            var content = await File.ReadAllTextAsync(destinationPath, cancellationToken);
            var relative = Path.GetRelativePath(vaultPath, destinationPath).Replace('\\', '/');
            return new VaultMutationReceipt($"note:{relative}", relative, VaultRevision.Compute(content));
        }
    }
}
