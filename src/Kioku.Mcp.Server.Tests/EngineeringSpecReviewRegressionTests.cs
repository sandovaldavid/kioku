using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class EngineeringSpecReviewRegressionTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Theory]
    [InlineData("C# integration")]
    [InlineData("api.v2")]
    [InlineData("design.md")]
    [InlineData("alpha..beta")]
    public async Task PlanSpecReference_RoundTripsEveryCreatedBasename(string title)
    {
        var (service, workspace) = CreateService();
        var created = await service.CreateSpecAsync(
            "demo", title, "objective", "requirements", status: "approved");
        Assert.StartsWith("[ok]", created);

        var specPath = Assert.Single(
            Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md"));
        var basename = Path.GetFileNameWithoutExtension(specPath);

        var linked = await service.CreatePlanFromSpecAsync(
            "demo", "Round trip plan", "objective", "- [ ] step", basename);

        Assert.StartsWith("[ok]", linked);
        Assert.Contains($"spec: [[{basename}]]", linked);
    }

    [Fact]
    public async Task PlanSpecReference_RejectsHeadingSyntax_WhenNoLiteralBasenameMatches()
    {
        var (service, workspace) = CreateService();
        await service.CreateSpecAsync(
            "demo", "Heading base", "objective", "requirements", status: "approved");
        var basename = Path.GetFileNameWithoutExtension(
            Assert.Single(Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md")));

        var result = await service.CreatePlanFromSpecAsync(
            "demo", "Heading plan", "objective", "- [ ] step", basename + "#details");

        Assert.Contains("Invalid spec reference", result);
    }

    [Fact]
    public async Task PlanSpecRelation_IsFrontmatterOnly_NotAnImplicitBodyVariable()
    {
        var (service, workspace) = CreateService();
        await service.CreateSpecAsync(
            "demo", "Approved", "objective", "requirements", status: "approved");
        var basename = Path.GetFileNameWithoutExtension(
            Assert.Single(Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md")));

        await File.WriteAllTextAsync(
            workspace.GetVaultTemplatePath("plan")!,
            "# {{title}}\n\nSpec body token: {{spec}}\n\n## Objective\n\n{{objective}}\n\n## Steps\n\n{{steps}}",
            Encoding.UTF8);

        var result = await service.CreatePlanFromSpecAsync(
            "demo", "Frontmatter relation", "objective", "- [ ] step", basename);
        Assert.StartsWith("[ok]", result);

        var planPath = Assert.Single(
            Directory.GetFiles(workspace.GetSubfolder("demo", "plans"), "PLAN-*.md"));
        var content = await File.ReadAllTextAsync(planPath);
        Assert.Contains($"spec: \"[[{basename}]]\"", content);
        Assert.Contains("Spec body token: {{spec}}", content);
        Assert.DoesNotContain(
            ProjectWorkspaceService.SupportedVariablesFor("plan"),
            variable => string.Equals(variable, "spec", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Templater_IdempotentRetry_DoesNotReplaySpecOrPlanEvaluation()
    {
        var bridge = new ObsidianBridgeService(
            NullLogger<ObsidianBridgeService>.Instance,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath, ObsidianBridgePort = 1 });
        var (service, workspace) = CreateService(bridge);

        await workspace.EnsureProjectScaffoldAsync("demo");
        await File.WriteAllTextAsync(
            workspace.GetVaultTemplatePath("spec")!,
            "# {{title}}\n\n<% tp.date.now() %>\n\n## Objective\n{{objective}}\n\n## Requirements\n{{requirements}}",
            Encoding.UTF8);
        var specMutation = VaultMutationPreconditions.FromToolArguments(
            mutationId: "templater-spec-retry");

        var specFirst = await service.CreateSpecAsync(
            "demo", "Templater retry", "objective", "requirements",
            status: "approved", preconditions: specMutation);
        var specRetry = await service.CreateSpecAsync(
            "demo", "Templater retry", "objective", "requirements",
            status: "approved", preconditions: specMutation);

        Assert.Contains("[warning] template contains Templater syntax", specFirst);
        Assert.DoesNotContain("template contains Templater syntax", specRetry);

        var specName = Path.GetFileNameWithoutExtension(
            Assert.Single(Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md")));
        await File.WriteAllTextAsync(
            workspace.GetVaultTemplatePath("plan")!,
            "# {{title}}\n\n<% tp.date.now() %>\n\n## Objective\n{{objective}}\n\n## Steps\n{{steps}}",
            Encoding.UTF8);
        var planMutation = VaultMutationPreconditions.FromToolArguments(
            mutationId: "templater-plan-retry");

        var planFirst = await service.CreatePlanFromSpecAsync(
            "demo", "Templater plan retry", "objective", "- [ ] step",
            specName, preconditions: planMutation);
        var planRetry = await service.CreatePlanFromSpecAsync(
            "demo", "Templater plan retry", "objective", "- [ ] step",
            specName, preconditions: planMutation);

        Assert.Contains("[warning] template contains Templater syntax", planFirst);
        Assert.DoesNotContain("template contains Templater syntax", planRetry);
    }

    [Fact]
    public async Task TemplaterApplied_ReturnsFinalRevision_ForSpecAndLinkedPlan()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var bridge = new ObsidianBridgeService(
            NullLogger<ObsidianBridgeService>.Instance,
            new KiokuConfiguration
            {
                VaultPath = _fixture.VaultPath,
                ObsidianBridgePort = server.Port,
            });
        var (service, workspace) = CreateService(bridge);

        await workspace.EnsureProjectScaffoldAsync("demo");
        await File.WriteAllTextAsync(
            workspace.GetVaultTemplatePath("spec")!,
            "# {{title}}\n\n<% tp.date.now() %>\n\n## Objective\n{{objective}}\n\n## Requirements\n{{requirements}}",
            Encoding.UTF8);
        await File.WriteAllTextAsync(
            workspace.GetVaultTemplatePath("plan")!,
            "# {{title}}\n\n<% tp.date.now() %>\n\n## Objective\n{{objective}}\n\n## Steps\n{{steps}}",
            Encoding.UTF8);

        var serverSide = Task.Run(async () =>
        {
            var socket = await server.AcceptAuthenticatedConnectionAsync();
            for (var i = 0; i < 2; i++)
            {
                var raw = await server.ReceiveAsync(socket);
                using var document = JsonDocument.Parse(raw);
                var message = document.RootElement;
                Assert.Equal(
                    "evaluate-templater-in-file",
                    message.GetProperty("command").GetString());

                var requestId = message.GetProperty("requestId").GetString();
                var notePath = message.GetProperty("payload").GetProperty("notePath").GetString()!;
                var absolutePath = Path.Combine(
                    _fixture.VaultPath,
                    notePath.Replace('/', Path.DirectorySeparatorChar));
                var current = await File.ReadAllTextAsync(absolutePath);
                await File.WriteAllTextAsync(
                    absolutePath,
                    current.Replace(
                        "<% tp.date.now() %>",
                        $"evaluated-{i}",
                        StringComparison.Ordinal),
                    NoteHelpers.Utf8NoBom);

                await server.SendAsync(socket, JsonSerializer.Serialize(new
                {
                    requestId,
                    success = true,
                    data = new { path = notePath },
                    error = (string?)null,
                    protocolVersion = 3,
                }));
            }
        });

        var specResult = await service.CreateSpecAsync(
            "demo", "Applied spec", "objective", "requirements", status: "approved");
        var specPath = Assert.Single(
            Directory.GetFiles(workspace.GetSubfolder("demo", "specs"), "SPEC-*.md"));
        var specName = Path.GetFileNameWithoutExtension(specPath);

        var planResult = await service.CreatePlanFromSpecAsync(
            "demo", "Applied plan", "objective", "- [ ] step", specName);
        await serverSide;

        var finalSpec = await File.ReadAllTextAsync(specPath);
        var planPath = Assert.Single(
            Directory.GetFiles(workspace.GetSubfolder("demo", "plans"), "PLAN-*.md"));
        var finalPlan = await File.ReadAllTextAsync(planPath);

        Assert.Contains($"revision: {VaultRevision.Compute(finalSpec)}", specResult);
        Assert.Contains($"revision: {VaultRevision.Compute(finalPlan)}", planResult);
        Assert.Contains("evaluated-0", finalSpec);
        Assert.Contains("evaluated-1", finalPlan);
    }

    private (EngineeringSpecService Service, ProjectWorkspaceService Workspace) CreateService(
        ObsidianBridgeService? bridge = null)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(
            config,
            NullLogger<VaultConfigService>.Instance);
        bridge ??= new ObsidianBridgeService(
            NullLogger<ObsidianBridgeService>.Instance,
            config);
        var mutations = new TestMutationService(_fixture.VaultPath);
        var workspace = new ProjectWorkspaceService(
            config,
            vaultConfig,
            bridge,
            mutations);
        var service = new EngineeringSpecService(
            workspace,
            vaultConfig,
            _fixture.Index,
            bridge,
            mutations);
        return (service, workspace);
    }

    private sealed class TestMutationService(string vaultPath) : IVaultMutationService
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (string Path, string Content, VaultMutationReceipt Receipt)> _mutations = [];

        public Task<VaultMutationReceipt> CreateTextAsync(
            string path,
            string content,
            VaultMutationPreconditions? preconditions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!string.IsNullOrWhiteSpace(preconditions?.MutationId) &&
                    _mutations.TryGetValue(preconditions.MutationId, out var existing))
                {
                    if (existing.Path != path || existing.Content != content)
                    {
                        throw new VaultMutationException(
                            VaultMutationErrorCodes.MutationIdReused,
                            "Mutation id reused with different input.");
                    }

                    return Task.FromResult(existing.Receipt with { AlreadyApplied = true });
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    using var writer = new StreamWriter(stream, NoteHelpers.Utf8NoBom);
                    writer.Write(content);
                }
                catch (IOException)
                {
                    throw new VaultMutationException(
                        VaultMutationErrorCodes.WriteConflict,
                        "Target already exists.");
                }

                var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
                var receipt = new VaultMutationReceipt(
                    $"note:{relative}",
                    relative,
                    VaultRevision.Compute(content));
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
            await File.WriteAllTextAsync(
                path,
                content,
                NoteHelpers.Utf8NoBom,
                cancellationToken);
            var relative = Path.GetRelativePath(vaultPath, path).Replace('\\', '/');
            return new VaultMutationReceipt(
                $"note:{relative}",
                relative,
                VaultRevision.Compute(content));
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
            return Task.FromResult(new VaultMutationReceipt(
                $"note:{relative}",
                relative,
                null));
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
                await File.WriteAllTextAsync(
                    destinationPath,
                    replacementContent,
                    NoteHelpers.Utf8NoBom,
                    cancellationToken);
            }

            var content = await File.ReadAllTextAsync(destinationPath, cancellationToken);
            var relative = Path.GetRelativePath(vaultPath, destinationPath).Replace('\\', '/');
            return new VaultMutationReceipt(
                $"note:{relative}",
                relative,
                VaultRevision.Compute(content));
        }
    }
}
