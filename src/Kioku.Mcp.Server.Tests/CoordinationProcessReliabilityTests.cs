using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Domain.Coordination;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Proves coordination behavior across independent server processes and real MCP transports.
/// </summary>
public sealed class CoordinationProcessReliabilityTests : IAsyncLifetime
{
    private readonly CoordinationProcessVault vault = new();

    public Task InitializeAsync() => vault.InitializeAsync();

    public Task DisposeAsync() => vault.DisposeAsync();

    [Fact]
    public async Task StdioCrashAfterEventDurability_IsReplayedAfterRestart()
    {
        var identity = CreateIdentity("durability");
        var signal = Path.Combine(vault.VaultPath, ".test", "event-durable.signal");
        var request = CreateWorkItemRequest(identity);
        ProcessExitObservation observation;

        await using (var crashed = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-durability-writer",
                           new CoordinationFaultPlan(
                               "AfterEventDurabilityBeforeProjection",
                               "crash",
                               SignalPath: signal)))
        {
            var call = crashed.CallToolAsync("create_coordination_work_item", request);
            await CoordinationProcessAssertions.WaitForFileAsync(signal, TimeSpan.FromSeconds(10));
            await crashed.WaitForExitAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => call);
            observation = crashed.Observation;
        }

        Assert.True(observation.Exited);
        Assert.NotEqual(0, observation.ExitCode.GetValueOrDefault());

        await using var recovered = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-durability-recovery");
        using var snapshot = await recovered.CallJsonToolAsync(
            "get_coordination_work_item",
            new Dictionary<string, object?>
            {
                ["run_id"] = identity.RunId,
                ["work_item_id"] = identity.WorkItemId,
            });

        Assert.Equal(
            "pending",
            snapshot.RootElement.GetProperty("projection").GetProperty("state").GetString());
        Assert.Single(
            EnumerateEventFiles(vault.VaultPath),
            path => File.ReadAllText(path).Contains(identity.WorkItemId, StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(
            vault.VaultPath,
            ".kioku",
            "coordination",
            "snapshots",
            "work-items",
            $"{identity.WorkItemId}.json")));
    }

    [Fact]
    public async Task RestartPreservesDuplicateReplayAndFailsClosedOnTruncatedHistory()
    {
        var identity = CreateIdentity("replay");
        var request = CreateWorkItemRequest(identity);

        await using (var first = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-replay-first"))
        {
            using var created = await first.CallJsonToolAsync(
                "create_coordination_work_item",
                request);
            Assert.Equal(
                identity.WorkItemId,
                created.RootElement.GetProperty("projection").GetProperty("work_item_id").GetString());
        }

        var projectionPath = Path.Combine(
            vault.VaultPath,
            ".kioku",
            "coordination",
            "snapshots",
            "work-items",
            $"{identity.WorkItemId}.json");
        File.Delete(projectionPath);

        await using (var restarted = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-replay-restarted"))
        {
            using var duplicate = await restarted.CallJsonToolAsync(
                "create_coordination_work_item",
                request);
            var history = await restarted.CallJsonToolAsync(
                "list_coordination_history",
                new Dictionary<string, object?>
                {
                    ["run_id"] = identity.RunId,
                    ["work_item_id"] = identity.WorkItemId,
                });

            Assert.Equal(
                "pending",
                duplicate.RootElement.GetProperty("projection").GetProperty("state").GetString());
            Assert.Single(history.RootElement.GetProperty("items").EnumerateArray());
            Assert.True(File.Exists(projectionPath));
        }

        await File.WriteAllTextAsync(projectionPath, "{\"corrupt\":true}");
        await using (var corruptProjection = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-replay-projection"))
        {
            using var rebuilt = await corruptProjection.CallJsonToolAsync(
                "get_coordination_work_item",
                new Dictionary<string, object?>
                {
                    ["run_id"] = identity.RunId,
                    ["work_item_id"] = identity.WorkItemId,
                });
            Assert.Equal(
                "pending",
                rebuilt.RootElement.GetProperty("projection").GetProperty("state").GetString());
        }

        var eventPath = EnumerateEventFiles(vault.VaultPath)
            .Single(path => File.ReadAllText(path).Contains(identity.WorkItemId, StringComparison.Ordinal));
        var duplicatePath = Path.Combine(
            Path.GetDirectoryName(eventPath)!,
            "conflicting-duplicate.json");
        File.Copy(eventPath, duplicatePath);

        await using (var duplicateHistory = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-replay-duplicate"))
        {
            var duplicateResponse = await duplicateHistory.CallToolAsync(
                "get_coordination_work_item",
                new Dictionary<string, object?>
                {
                    ["run_id"] = identity.RunId,
                    ["work_item_id"] = identity.WorkItemId,
                });
            CoordinationProcessAssertions.AssertError(duplicateResponse, "CORRUPT_HISTORY");
        }

        File.Delete(duplicatePath);
        await File.WriteAllTextAsync(eventPath, "{\"truncated\":");

        await using var corrupt = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-replay-corrupt");
        var response = await corrupt.CallToolAsync(
            "get_coordination_work_item",
            new Dictionary<string, object?>
            {
                ["run_id"] = identity.RunId,
                ["work_item_id"] = identity.WorkItemId,
            });

        CoordinationProcessAssertions.AssertError(response, "CORRUPT_HISTORY");
        Assert.True(File.Exists(eventPath));
    }

    [Fact]
    public async Task TwoIndependentProcesses_RaceForOneClaimWithExactlyOneOwner()
    {
        var identity = CreateIdentity("claim-race");
        await using var first = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-claim-owner-a");
        await first.CallToolAsync(
            "create_coordination_work_item",
            CreateWorkItemRequest(identity, resourceScope: "logical:queue/main"));
        await using var second = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-claim-owner-b");
        Assert.NotEqual(first.ProcessId, second.ProcessId);

        var responses = await Task.WhenAll(
            first.CallToolAsync("acquire_coordination_claim", CreateClaimRequest(
                identity,
                attemptId: "attempt-a",
                sessionId: "session-a",
                transitionId: "claim-a")),
            second.CallToolAsync("acquire_coordination_claim", CreateClaimRequest(
                identity,
                attemptId: "attempt-b",
                sessionId: "session-b",
                transitionId: "claim-b")));

        var successful = responses.Where(response => response.StartsWith('{')).ToArray();
        var conflicts = responses.Where(response => response.StartsWith("[error:CLAIM_CONFLICT]", StringComparison.Ordinal))
            .ToArray();
        Assert.True(successful.Length == 1, string.Join(Environment.NewLine, responses));
        Assert.Single(conflicts);
        using var claim = JsonDocument.Parse(successful[0]);
        Assert.Equal(
            (int)CoordinationClaimDisposition.Acquired,
            claim.RootElement.GetProperty("disposition").GetInt32());

        using var history = await first.CallJsonToolAsync(
            "list_coordination_history",
            new Dictionary<string, object?>
            {
                ["run_id"] = identity.RunId,
                ["work_item_id"] = identity.WorkItemId,
            });
        Assert.Equal(2, history.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task TwoIndependentProcesses_RaceForOneCasRevisionWithExactlyOneCommit()
    {
        const string note = "Coordination/CasRace.md";
        await using var first = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-cas-owner-a");
        var createResponse = await first.CallToolAsync(
            "create_note",
            new Dictionary<string, object?>
            {
                ["name"] = note,
                ["content"] = "initial",
            });
        Assert.StartsWith("[ok]", createResponse, StringComparison.Ordinal);
        var revision = VaultRevision.Compute(await File.ReadAllTextAsync(vault.NotePath(note)));

        await using var second = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-cas-owner-b");
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        var responses = await Task.WhenAll(
            first.CallToolResultAsync(
                "edit_note",
                new Dictionary<string, object?>
                {
                    ["note"] = note,
                    ["content"] = "process A commit",
                    ["expected_revision"] = revision,
                }),
            second.CallToolResultAsync(
                "edit_note",
                new Dictionary<string, object?>
                {
                    ["note"] = note,
                    ["content"] = "process B commit",
                    ["expected_revision"] = revision,
                }));

        var successful = responses.Count(response => response.Text.StartsWith("[ok]", StringComparison.Ordinal));
        var conflicts = responses.Where(response =>
                response.Text.StartsWith("[error:WRITE_CONFLICT]", StringComparison.Ordinal) &&
                response.IsError &&
                response.StructuredContent is { ValueKind: JsonValueKind.Object })
            .ToArray();
        if (successful != 1 || conflicts.Length != 1)
        {
            var diagnostics = await Task.WhenAll(
                first.CaptureStandardErrorAsync(),
                second.CaptureStandardErrorAsync());
            throw new Xunit.Sdk.XunitException(
                "CAS race did not preserve exactly one success and one structured WRITE_CONFLICT." +
                $"{Environment.NewLine}{string.Join(Environment.NewLine, responses.Select(response => response.Describe()))}" +
                $"{Environment.NewLine}Process A stderr: {diagnostics[0]}" +
                $"{Environment.NewLine}Process B stderr: {diagnostics[1]}");
        }
        var finalContent = await File.ReadAllTextAsync(vault.NotePath(note));
        Assert.True(
            finalContent.Contains("process A commit", StringComparison.Ordinal) ^
            finalContent.Contains("process B commit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartAfterEachLegalTransition_PreservesTheCurrentProjection()
    {
        var identity = CreateIdentity("restart-transitions");
        var request = CreateWorkItemRequest(identity, resourceScope: "logical:restart");
        await using (var creator = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-restart-creator"))
        {
            await creator.CallToolAsync("create_coordination_work_item", request);
        }

        await AssertStateAfterRestartAsync(identity, "pending", "coordination-restart-pending");

        string claimId;
        long fenceGeneration;
        await using (var claimer = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-restart-claimer"))
        {
            using var claim = await claimer.CallJsonToolAsync(
                "acquire_coordination_claim",
                CreateClaimRequest(
                    identity,
                    attemptId: identity.AttemptId,
                    sessionId: identity.SessionId,
                    transitionId: "restart-claim"));
            claimId = claim.RootElement.GetProperty("claim").GetProperty("claim_id").GetString()!;
            fenceGeneration = claim.RootElement.GetProperty("claim").GetProperty("fence_generation").GetInt64();
        }

        await AssertStateAfterRestartAsync(identity, "claimed", "coordination-restart-claimed");

        await using (var runner = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-restart-runner"))
        {
            using var transition = await runner.CallJsonToolAsync(
                "transition_coordination_work_item",
                CreateTransitionRequest(
                    identity,
                    "running",
                    "restart-running",
                    claimId,
                    fenceGeneration,
                    expectedStateVersion: 1));
            Assert.Equal(
                "running",
                transition.RootElement.GetProperty("work_item").GetProperty("projection").GetProperty("state")
                    .GetString());
        }

        await AssertStateAfterRestartAsync(identity, "running", "coordination-restart-running");

        await using (var finisher = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-restart-finisher"))
        {
            using var transition = await finisher.CallJsonToolAsync(
                "transition_coordination_work_item",
                CreateTransitionRequest(
                    identity,
                    "completed",
                    "restart-completed",
                    claimId,
                    fenceGeneration,
                    expectedStateVersion: 2));
            Assert.Equal(
                "completed",
                transition.RootElement.GetProperty("work_item").GetProperty("projection").GetProperty("state")
                    .GetString());
        }

        await AssertStateAfterRestartAsync(identity, "completed", "coordination-restart-completed");
    }

    [Fact]
    public async Task LeaseTakeover_FencesRenewTransitionAndNoteMutationFromStaleProcess()
    {
        var identity = CreateIdentity("fencing");
        const string note = "Coordination/Fenced.md";
        await using var owner = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-stale-owner");
        var createNote = await owner.CallToolAsync(
            "create_note",
            new Dictionary<string, object?>
            {
                ["name"] = note,
                ["content"] = "original",
            });
        Assert.StartsWith("[ok]", createNote, StringComparison.Ordinal);
        var notePath = vault.NotePath(note);
        var revision = VaultRevision.Compute(await File.ReadAllTextAsync(notePath));

        await owner.CallToolAsync(
            "create_coordination_work_item",
            CreateWorkItemRequest(identity, resourceScope: "note:Coordination/Fenced.md"));
        using var firstClaim = await owner.CallJsonToolAsync(
            "acquire_coordination_claim",
            CreateClaimRequest(
                identity,
                attemptId: "attempt-a",
                sessionId: "session-a",
                transitionId: "claim-a",
                leaseSeconds: 1,
                resourceKey: "note:Coordination/Fenced.md"));
        var oldClaim = firstClaim.RootElement.GetProperty("claim");
        var oldClaimId = oldClaim.GetProperty("claim_id").GetString()!;
        var oldFence = oldClaim.GetProperty("fence_generation").GetInt64();

        await using var successor = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-successor");
        using var successorClaim = await WaitForTakeoverAsync(successor, identity);
        Assert.Equal(
            (int)CoordinationClaimDisposition.TakenOver,
            successorClaim.RootElement.GetProperty("disposition").GetInt32());

        var renewResponse = await owner.CallToolAsync(
            "renew_coordination_claim",
            CreateMutationRequest(
                identity,
                oldClaimId,
                oldFence,
                attemptId: "attempt-a",
                sessionId: "session-a",
                transitionId: "renew-stale",
                resourceKey: "note:Coordination/Fenced.md"));
        CoordinationProcessAssertions.AssertError(renewResponse, "CLAIM_SUPERSEDED");

        var transitionResponse = await owner.CallToolAsync(
            "transition_coordination_work_item",
            new Dictionary<string, object?>
            {
                ["run_id"] = identity.RunId,
                ["work_item_id"] = identity.WorkItemId,
                ["next_state"] = "running",
                ["attempt_id"] = "attempt-a",
                ["session_id"] = "session-a",
                ["transition_id"] = "transition-stale",
                ["claim_id"] = oldClaimId,
                ["fence_generation"] = oldFence,
            });
        CoordinationProcessAssertions.AssertError(transitionResponse, "CLAIM_SUPERSEDED");

        var mutationResponse = await owner.CallToolAsync(
            "edit_note",
            new Dictionary<string, object?>
            {
                ["note"] = note,
                ["content"] = "stale owner write",
                ["expected_revision"] = revision,
                ["claim_id"] = oldClaimId,
                ["fence_generation"] = oldFence,
                ["resource_key"] = "note:Coordination/Fenced.md",
                ["mutation_id"] = "stale-owner-mutation",
            });
        Assert.Contains("STALE_FENCE", mutationResponse, StringComparison.Ordinal);
        Assert.Contains("original", await File.ReadAllTextAsync(notePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalEditAfterCasValidation_IsRejectedWithoutOverwritingTheEdit()
    {
        const string note = "Coordination/External.md";
        await using var initial = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-cas-initial");
        var createResponse = await initial.CallToolAsync(
            "create_note",
            new Dictionary<string, object?>
            {
                ["name"] = note,
                ["content"] = "initial",
            });
        Assert.StartsWith("[ok]", createResponse, StringComparison.Ordinal);
        var notePath = vault.NotePath(note);
        var revision = VaultRevision.Compute(await File.ReadAllTextAsync(notePath));

        var signal = Path.Combine(vault.VaultPath, ".test", "cas.signal");
        var release = Path.Combine(vault.VaultPath, ".test", "cas.release");
        await using var paused = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-cas-writer",
            new CoordinationFaultPlan(
                "AfterCasValidationBeforeWrite",
                "pause",
                SignalPath: signal,
                ReleasePath: release));
        var write = paused.CallToolAsync(
            "edit_note",
            new Dictionary<string, object?>
            {
                ["note"] = note,
                ["content"] = "stale write",
                ["expected_revision"] = revision,
            });
        await CoordinationProcessAssertions.WaitForFileAsync(signal, TimeSpan.FromSeconds(10));
        await File.WriteAllTextAsync(notePath, "external Obsidian edit");
        await File.WriteAllTextAsync(release, "release");

        var response = await write;
        CoordinationProcessAssertions.AssertError(response, "WRITE_CONFLICT");
        Assert.Equal("external Obsidian edit", await File.ReadAllTextAsync(notePath));
    }

    [Fact]
    public async Task CrashAfterTargetWrite_LeavesNoteRecoverableOnColdRestart()
    {
        const string note = "Coordination/Crash.md";
        var signal = Path.Combine(vault.VaultPath, ".test", "target-write.signal");
        ProcessExitObservation observation;

        await using (var crashed = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-target-writer",
                           new CoordinationFaultPlan(
                               "AfterTargetWriteBeforeReindex",
                               "crash",
                               SignalPath: signal)))
        {
            var call = crashed.CallToolAsync(
                "create_note",
                new Dictionary<string, object?>
                {
                    ["name"] = note,
                    ["content"] = "durable target write",
                });
            await CoordinationProcessAssertions.WaitForFileAsync(signal, TimeSpan.FromSeconds(10));
            await crashed.WaitForExitAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => call);
            observation = crashed.Observation;
        }

        Assert.NotEqual(0, observation.ExitCode.GetValueOrDefault());
        Assert.Contains("durable target write", await File.ReadAllTextAsync(vault.NotePath(note)));

        await using var recovered = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-target-recovery");
        var rebuild = await recovered.CallToolAsync("rebuild_index", new Dictionary<string, object?>());
        Assert.StartsWith("[ok] Re-indexing completed.", rebuild, StringComparison.Ordinal);
        var response = await recovered.CallToolAsync(
            "read_note",
            new Dictionary<string, object?>
            {
                ["note"] = note,
            });
        Assert.Contains("durable target write", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledMutation_LeavesTheValidatedTargetUntouched()
    {
        const string note = "Coordination/Cancelled.md";
        await using var initial = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-cancel-initial");
        var createResponse = await initial.CallToolAsync(
            "create_note",
            new Dictionary<string, object?>
            {
                ["name"] = note,
                ["content"] = "initial",
            });
        Assert.StartsWith("[ok]", createResponse, StringComparison.Ordinal);
        var revision = VaultRevision.Compute(await File.ReadAllTextAsync(vault.NotePath(note)));

        var signal = Path.Combine(vault.VaultPath, ".test", "cancel.signal");
        await using var paused = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-cancel-writer",
            new CoordinationFaultPlan(
                "AfterCasValidationBeforeWrite",
                "pause",
                SignalPath: signal,
                ReleasePath: Path.Combine(vault.VaultPath, ".test", "cancel.release")));
        using var cancellation = new CancellationTokenSource();
        var write = paused.CallToolAsync(
            "edit_note",
            new Dictionary<string, object?>
            {
                ["note"] = note,
                ["content"] = "cancelled write",
                ["expected_revision"] = revision,
            },
            cancellation.Token);
        await CoordinationProcessAssertions.WaitForFileAsync(signal, TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        var finalContent = await File.ReadAllTextAsync(vault.NotePath(note));
        Assert.Contains("initial", finalContent, StringComparison.Ordinal);
        Assert.DoesNotContain("cancelled write", finalContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StdioEof_ShutsDownCleanlyAndPersistsCoordinationState()
    {
        var identity = CreateIdentity("shutdown");
        await using var server = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-graceful-shutdown");
        using var created = await server.CallJsonToolAsync(
            "create_coordination_work_item",
            CreateWorkItemRequest(identity));

        var observation = await server.ShutdownGracefullyAsync();

        Assert.True(observation.Exited);
        Assert.Equal(0, observation.ExitCode);
        Assert.Equal("normal-exit", observation.Cause);

        await using var recovered = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-after-shutdown");
        using var snapshot = await recovered.CallJsonToolAsync(
            "get_coordination_work_item",
            new Dictionary<string, object?>
            {
                ["run_id"] = identity.RunId,
                ["work_item_id"] = identity.WorkItemId,
            });
        Assert.Equal(
            "pending",
            snapshot.RootElement.GetProperty("projection").GetProperty("state").GetString());
    }

    [Fact]
    public async Task StreamableHttpProcess_ReadsAndPersistsCoordinationState()
    {
        var identity = CreateIdentity("http");
        await using var server = await CoordinationProcessServer.StartHttpAsync(
            vault.VaultPath,
            "coordination-http-client");

        using var created = await server.CallJsonToolAsync(
            "create_coordination_work_item",
            CreateWorkItemRequest(identity));
        using var fetched = await server.CallJsonToolAsync(
            "get_coordination_work_item",
            new Dictionary<string, object?>
            {
                ["run_id"] = identity.RunId,
                ["work_item_id"] = identity.WorkItemId,
            });

        Assert.Equal(
            "pending",
            created.RootElement.GetProperty("projection").GetProperty("state").GetString());
        Assert.Equal(
            "pending",
            fetched.RootElement.GetProperty("projection").GetProperty("state").GetString());
        Assert.True(server.ProcessId > 0);
    }

    [Fact]
    public async Task LegacySessionFixture_RemainsReadableAcrossAProcessRestart()
    {
        var sessionPath = Path.Combine(vault.VaultPath, "Sessions", "Legacy Session.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
        await File.WriteAllTextAsync(
            sessionPath,
            "---\ntags:\n  - session\ntype: session\nstatus: done\ndate: 2026-07-18\n" +
            "session_id: 019c0000-0000-7000-8000-000000000001\nagent: legacy-agent\n" +
            "client_name: legacy-client\nstarted_at: \"2026-07-18T20:00:00.0000000Z\"\n" +
            "ended_at: \"2026-07-18T20:30:00.0000000Z\"\ncustom_field: preserve-me\n---\n\n" +
            "# Legacy Session\n\nThe legacy session remains readable.\n");

        await using (var first = await CoordinationProcessServer.StartStdioAsync(
                           vault.VaultPath,
                           "coordination-legacy-reader-a"))
        {
            var response = await first.CallToolAsync(
                "list_work_sessions",
                new Dictionary<string, object?>
                {
                    ["sessions_folder"] = "Sessions",
                });
            Assert.Contains("019c0000-0000-7000-8000-000000000001", response, StringComparison.Ordinal);
            Assert.Contains("Legacy Session", response, StringComparison.Ordinal);
        }

        await using var restarted = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            "coordination-legacy-reader-b");
        var restartedResponse = await restarted.CallToolAsync(
            "read_note",
            new Dictionary<string, object?>
            {
                ["note"] = "Sessions/Legacy Session",
            });
        Assert.Contains("legacy session remains readable", restartedResponse, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> WaitForTakeoverAsync(
        CoordinationProcessServer successor,
        ProcessIdentity identity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var response = await successor.CallToolAsync(
                "acquire_coordination_claim",
                CreateClaimRequest(
                    identity,
                    attemptId: "attempt-b",
                    sessionId: "session-b",
                    transitionId: "claim-b",
                    leaseSeconds: 30,
                    resourceKey: "note:Coordination/Fenced.md"),
                timeout.Token);
            if (response.StartsWith('{'))
            {
                return JsonDocument.Parse(response);
            }

            CoordinationProcessAssertions.AssertError(response, "CLAIM_CONFLICT");
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }

    private async Task AssertStateAfterRestartAsync(
        ProcessIdentity identity,
        string expectedState,
        string clientName)
    {
        await using var restarted = await CoordinationProcessServer.StartStdioAsync(
            vault.VaultPath,
            clientName);
        using var snapshot = await restarted.CallJsonToolAsync(
            "get_coordination_work_item",
            new Dictionary<string, object?>
            {
                ["run_id"] = identity.RunId,
                ["work_item_id"] = identity.WorkItemId,
            });
        Assert.Equal(
            expectedState,
            snapshot.RootElement.GetProperty("projection").GetProperty("state").GetString());
    }

    private static Dictionary<string, object?> CreateWorkItemRequest(
        ProcessIdentity identity,
        string resourceScope = "logical:queue/main") =>
        new Dictionary<string, object?>
        {
            ["project"] = "coordination-process-tests",
            ["run_id"] = identity.RunId,
            ["work_item_id"] = identity.WorkItemId,
            ["attempt_id"] = identity.AttemptId,
            ["session_id"] = identity.SessionId,
            ["agent"] = "test-agent",
            ["resource_scope"] = resourceScope,
            ["summary"] = "Synthetic coordination process fixture.",
            ["transition_id"] = identity.TransitionId,
        };

    private static Dictionary<string, object?> CreateClaimRequest(
        ProcessIdentity identity,
        string attemptId,
        string sessionId,
        string transitionId,
        int leaseSeconds = 30,
        string resourceKey = "logical:queue/main") =>
        new Dictionary<string, object?>
        {
            ["run_id"] = identity.RunId,
            ["work_item_id"] = identity.WorkItemId,
            ["attempt_id"] = attemptId,
            ["session_id"] = sessionId,
            ["resource_key"] = resourceKey,
            ["transition_id"] = transitionId,
            ["lease_seconds"] = leaseSeconds,
            ["agent"] = "test-agent",
        };

    private static Dictionary<string, object?> CreateMutationRequest(
        ProcessIdentity identity,
        string claimId,
        long fenceGeneration,
        string attemptId,
        string sessionId,
        string transitionId,
        string resourceKey) =>
        new Dictionary<string, object?>
        {
            ["run_id"] = identity.RunId,
            ["work_item_id"] = identity.WorkItemId,
            ["attempt_id"] = attemptId,
            ["session_id"] = sessionId,
            ["claim_id"] = claimId,
            ["resource_key"] = resourceKey,
            ["fence_generation"] = fenceGeneration,
            ["transition_id"] = transitionId,
            ["lease_seconds"] = 30,
            ["agent"] = "test-agent",
        };

    private static Dictionary<string, object?> CreateTransitionRequest(
        ProcessIdentity identity,
        string nextState,
        string transitionId,
        string claimId,
        long fenceGeneration,
        long expectedStateVersion) =>
        new Dictionary<string, object?>
        {
            ["run_id"] = identity.RunId,
            ["work_item_id"] = identity.WorkItemId,
            ["next_state"] = nextState,
            ["attempt_id"] = identity.AttemptId,
            ["session_id"] = identity.SessionId,
            ["transition_id"] = transitionId,
            ["expected_state_version"] = expectedStateVersion,
            ["claim_id"] = claimId,
            ["fence_generation"] = fenceGeneration,
            ["agent"] = "test-agent",
        };

    private static ProcessIdentity CreateIdentity(string prefix) =>
        new(
            $"run-{prefix}-{Guid.NewGuid():N}",
            $"work-{prefix}-{Guid.NewGuid():N}",
            $"attempt-{prefix}-{Guid.NewGuid():N}",
            $"session-{prefix}-{Guid.NewGuid():N}",
            $"create-{prefix}-{Guid.NewGuid():N}");

    private static string[] EnumerateEventFiles(string vaultPath) =>
        Directory.GetFiles(
            Path.Combine(vaultPath, ".kioku", "coordination", "events"),
            "*.json",
            SearchOption.AllDirectories);

    private sealed record ProcessIdentity(
        string RunId,
        string WorkItemId,
        string AttemptId,
        string SessionId,
        string TransitionId);
}
