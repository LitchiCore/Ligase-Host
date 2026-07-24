using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class LayoutContractV1JsonTests
{
    private const string VectorSha256 =
        "b8022f21d37481bc54a869a6be8c70b994cb94266796634356808c8dbe859fcd";

    [TestMethod]
    public void ResolutionVectorsMatchCanonicalResults()
    {
        using var document = LoadVectors();
        var fixtures = document.RootElement.GetProperty("descriptorFixtures");
        foreach (var testCase in document.RootElement
                     .GetProperty("resolutionCases")
                     .EnumerateArray())
        {
            var inputJson = testCase.TryGetProperty("input", out var directInput)
                ? directInput.GetRawText()
                : BuildRequestJson(testCase, fixtures);
            var actual = LayoutContractV1Json.SerializeResult(
                LayoutContractV1Json.Resolve(inputJson));
            var expected = JsonSerializer.Serialize(testCase.GetProperty("expected"));
            Assert.AreEqual(
                expected,
                actual,
                testCase.GetProperty("id").GetString());
        }
    }

    [TestMethod]
    public void BindingFailureOrderAndCanonicalOutputAreStable()
    {
        var descriptor = Descriptor(
            revision: 1,
            status: "published",
            minClientVersion: 2);
        var context = Context();

        var notFound = LayoutContractV1Resolver.Resolve(Request(
            context,
            [descriptor],
            binding: new LayoutBindingV1(
                "22222222-2222-4222-8222-222222222222",
                1)));
        Assert.AreEqual(
            "{\"code\":\"bindingNotFound\"}",
            LayoutContractV1Json.SerializeResult(notFound));

        var incompatible = LayoutContractV1Resolver.Resolve(Request(
            context,
            [descriptor],
            binding: new LayoutBindingV1(descriptor.LayoutId, 1)));
        Assert.AreEqual(
            "{\"code\":\"incompatibleBinding\"}",
            LayoutContractV1Json.SerializeResult(incompatible));
    }

    [TestMethod]
    public void VectorFileSha256IsFrozen()
    {
        var bytes = File.ReadAllBytes(VectorPath());
        Assert.AreEqual(
            VectorSha256,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static string BuildRequestJson(JsonElement testCase, JsonElement fixtures)
    {
        var request = testCase.GetProperty("request");
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["instance"] = new JsonObject
            {
                ["hostUniqueId"] = request.GetProperty("hostUniqueId").GetString(),
                ["appUuid"] = request.GetProperty("appUuid").GetString()
            }
        };
        CopyOptional(request, root, "portableIdentity");
        CopyOptional(request, root, "layoutBinding");
        root["context"] = JsonNode.Parse(request.GetProperty("context").GetRawText());

        var descriptors = new JsonArray();
        foreach (var reference in testCase.GetProperty("descriptorRefs").EnumerateArray())
        {
            descriptors.Add(JsonNode.Parse(
                fixtures.GetProperty(reference.GetString()!).GetRawText()));
        }
        root["descriptors"] = descriptors;
        return root.ToJsonString();
    }

    private static void CopyOptional(JsonElement source, JsonObject target, string property)
    {
        if (source.TryGetProperty(property, out var value))
            target[property] = JsonNode.Parse(value.GetRawText());
    }

    private static LayoutResolutionRequestV1 Request(
        LayoutResolutionContextV1 context,
        IReadOnlyList<LayoutDescriptorV1> descriptors,
        LayoutBindingV1? binding = null) =>
        new(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            new PortableGameIdentityV1("steam", "123"),
            binding,
            context,
            descriptors);

    private static LayoutResolutionContextV1 Context() =>
        new(1, 1, "touch", "phone", "portrait");

    private static LayoutDescriptorV1 Descriptor(
        long revision,
        string status,
        int minClientVersion = 1) =>
        new(
            1,
            "11111111-1111-4111-8111-111111111111",
            revision,
            [new PortableGameIdentityV1("steam", "123")],
            new LayoutCompatibilityV1(minClientVersion, 1),
            status,
            [
                new LayoutVariantV1(
                    "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
                    "touch",
                    ["phone"],
                    ["portrait"])
            ]);

    private static JsonDocument LoadVectors() =>
        JsonDocument.Parse(File.ReadAllBytes(VectorPath()));

    private static string VectorPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName,
                "tests",
                "fixtures",
                "layout-contract-v1-vectors.json");
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Layout contract vector file was not found.");
    }
}
