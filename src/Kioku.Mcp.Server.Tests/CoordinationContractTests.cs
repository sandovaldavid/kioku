using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain.Coordination;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class CoordinationContractTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Coordination");

    [Theory]
    [InlineData(CoordinationContractKind.HandoffPacket, "handoff-packet.json")]
    [InlineData(CoordinationContractKind.CoordinationEvent, "coordination-event.json")]
    [InlineData(CoordinationContractKind.CoordinationClaim, "coordination-claim.json")]
    [InlineData(CoordinationContractKind.CoordinationConflict, "coordination-conflict.json")]
    [InlineData(CoordinationContractKind.WorkItemProjection, "work-item-projection.json")]
    public async Task ValidFixtures_PassTheirEmbeddedSchema(CoordinationContractKind kind, string fileName)
    {
        var json = File.ReadAllText(Path.Combine(FixtureRoot, "valid", fileName));
        var result = await new CoordinationContractValidator().ValidateAsync(kind, json);

        Assert.True(result.IsValid, FormatErrors(result));
    }

    [Theory]
    [InlineData(CoordinationContractKind.HandoffPacket, "handoff-packet.json")]
    [InlineData(CoordinationContractKind.CoordinationEvent, "coordination-event.json")]
    [InlineData(CoordinationContractKind.CoordinationClaim, "coordination-claim.json")]
    [InlineData(CoordinationContractKind.CoordinationConflict, "coordination-conflict.json")]
    [InlineData(CoordinationContractKind.WorkItemProjection, "work-item-projection.json")]
    public async Task InvalidFixtures_ReturnStableValidationErrors(CoordinationContractKind kind, string fileName)
    {
        var json = File.ReadAllText(Path.Combine(FixtureRoot, "invalid", fileName));
        var result = await new CoordinationContractValidator().ValidateAsync(kind, json);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error =>
        {
            Assert.False(string.IsNullOrWhiteSpace(error.Path));
            Assert.False(string.IsNullOrWhiteSpace(error.Code));
        });
    }

    [Theory]
    [InlineData("handoff-packet.json")]
    [InlineData("coordination-event.json")]
    [InlineData("coordination-claim.json")]
    [InlineData("coordination-conflict.json")]
    [InlineData("work-item-projection.json")]
    public void ValidFixtures_HaveMatchingContentHashes(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "valid", fileName)));

        Assert.True(
            CanonicalJson.ContentHashMatches(document.RootElement, CoordinationContract.ContentHashPropertyName),
            $"Expected content_hash {CanonicalJson.ComputeSha256Hex(document.RootElement, CoordinationContract.ContentHashPropertyName)} in {fileName}");
    }

    [Fact]
    public void CanonicalJson_SortsObjectPropertiesAndPreservesArrayOrder()
    {
        using var document = JsonDocument.Parse("""
            {"z":1,"a":{"z":2,"a":true},"items":[{"b":2,"a":1},"two"]}
            """);

        var canonical = CanonicalJson.Serialize(document.RootElement);

        Assert.Equal("{\"a\":{\"a\":true,\"z\":2},\"items\":[{\"a\":1,\"b\":2},\"two\"],\"z\":1}", canonical);
    }

    [Fact]
    public void CanonicalJson_RejectsDuplicateObjectProperties()
    {
        using var document = JsonDocument.Parse("{\"value\":1,\"value\":2}");

        Assert.Throws<JsonException>(() => CanonicalJson.Serialize(document.RootElement));
    }

    [Fact]
    public void ContentHash_ExcludesOnlyRootHashAndUsesUppercaseSha256()
    {
        using var first = JsonDocument.Parse("""
            {"content_hash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","nested":{"content_hash":"keep"},"value":1}
            """);
        using var second = JsonDocument.Parse("""
            {"content_hash":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB","nested":{"content_hash":"keep"},"value":1}
            """);

        var firstHash = CanonicalJson.ComputeSha256Hex(first.RootElement, "content_hash");
        var secondHash = CanonicalJson.ComputeSha256Hex(second.RootElement, "content_hash");

        Assert.Equal(firstHash, secondHash);
        Assert.Equal(64, firstHash.Length);
        Assert.Equal(firstHash.ToUpperInvariant(), firstHash);
    }

    [Fact]
    public void ContractSerializer_ComputesAndVerifiesContentHash()
    {
        var packet = CreateHandoffPacket(string.Empty);
        var hash = CoordinationContractSerializer.ComputeContentHash(packet);
        var hashedPacket = CreateHandoffPacket(hash);

        using var document = JsonDocument.Parse(CoordinationContractSerializer.Serialize(hashedPacket));

        Assert.True(CoordinationContractSerializer.ContentHashMatches(document.RootElement));

        var altered = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        altered["state"] = "running";
        using var alteredDocument = JsonDocument.Parse(altered.ToJsonString());
        Assert.False(CoordinationContractSerializer.ContentHashMatches(alteredDocument.RootElement));
    }

    [Fact]
    public async Task UnknownFields_AreIgnoredByTypedReadersAndAcceptedBySchema()
    {
        var packet = CreateHandoffPacket(CoordinationContractSerializer.ComputeContentHash(CreateHandoffPacket(string.Empty)));
        var json = JsonNode.Parse(CoordinationContractSerializer.Serialize(packet))!.AsObject();
        json["future_field"] = new JsonObject { ["introduced_in"] = 2 };
        using (var documentWithExtension = JsonDocument.Parse(json.ToJsonString()))
        {
            json["content_hash"] = CanonicalJson.ComputeSha256Hex(
                documentWithExtension.RootElement,
                CoordinationContract.ContentHashPropertyName);
        }

        var typedPacket = JsonSerializer.Deserialize<HandoffPacket>(json.ToJsonString());
        var validation = await new CoordinationContractValidator().ValidateAsync(
            CoordinationContractKind.HandoffPacket,
            json.ToJsonString());

        Assert.NotNull(typedPacket);
        Assert.Equal(packet.WorkItemId, typedPacket!.WorkItemId);
        Assert.True(validation.IsValid, FormatErrors(validation));
    }

    [Fact]
    public async Task Validator_ReportsUnsupportedVersionAndHashMismatchWithStableCodes()
    {
        var validator = new CoordinationContractValidator();
        var unsupportedVersion = await validator.ValidateAsync(
            CoordinationContractKind.HandoffPacket,
            File.ReadAllText(Path.Combine(FixtureRoot, "invalid", "handoff-packet.json")));
        var hashMismatch = await validator.ValidateAsync(
            CoordinationContractKind.HandoffPacket,
            File.ReadAllText(Path.Combine(FixtureRoot, "valid", "handoff-packet.json"))
                .Replace(
                    "79403C2C84AFA881E68531A04230D6C49A52EED8950867040859DE544F7AF6D6",
                    "0000000000000000000000000000000000000000000000000000000000000000",
                    StringComparison.Ordinal));

        Assert.Contains(unsupportedVersion.Errors, error => error.Code == CoordinationContractErrorCodes.UnsupportedSchemaVersion);
        Assert.Contains(hashMismatch.Errors, error => error.Code == CoordinationContractErrorCodes.ContentHashMismatch);
    }

    [Fact]
    public void EventTypes_AreStableAndDoNotExposeAuthorityClaims()
    {
        Assert.Contains(CoordinationEventTypes.WorkItemStarted, CoordinationEventTypes.All);
        Assert.Contains(CoordinationEventTypes.WorkItemReopened, CoordinationEventTypes.All);
        Assert.DoesNotContain("caller.grants.authority", CoordinationEventTypes.All);
        Assert.DoesNotContain("agent", CoordinationAuthorityScopes.Read);
        Assert.DoesNotContain("client_name", CoordinationAuthorityScopes.Write);
    }

    private static HandoffPacket CreateHandoffPacket(string contentHash) => new()
    {
        RunId = "run-01",
        WorkItemId = "work-01",
        AttemptId = "attempt-01",
        SessionId = "session-01",
        ParentSessionId = null,
        Agent = "agent-a",
        ClientName = "client-a",
        Project = "example-project",
        ResourceScope = ["Notes/Plan.md"],
        AuthorityScope = [CoordinationAuthorityScopes.Read],
        State = CoordinationStates.Pending,
        Checkpoint = new HandoffCheckpoint
        {
            Summary = "Ready to begin the work item.",
            Reference = "session-01",
            Revision = 0,
            ContentHash = null,
        },
        NextActions =
        [
            new HandoffAction
            {
                ActionId = "action-01",
                Description = "Review the contract fixtures.",
                ResourceKey = "note:Notes/Plan.md",
                Status = "pending",
            },
        ],
        Artifacts = [],
        Blockers = [],
        Conflicts = [],
        CreatedAt = new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero),
        Sequence = 1,
        StateVersion = 0,
        Revision = 0,
        ContentHash = contentHash,
    };

    private static string FormatErrors(CoordinationValidationResult result) =>
        string.Join(", ", result.Errors.Select(error => $"{error.Path}: {error.Code}"));
}
