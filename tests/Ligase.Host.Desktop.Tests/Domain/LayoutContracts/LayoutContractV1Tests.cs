using System.Text.Json;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class LayoutContractV1Tests
{
    [TestMethod]
    public void NormalizationVectorsMatch()
    {
        using var document = LoadVectors();
        foreach (var testCase in document.RootElement
                     .GetProperty("normalizationCases")
                     .EnumerateArray())
        {
            var expected = testCase.GetProperty("expected");
            var kind = testCase.GetProperty("kind").GetString();
            bool valid;
            string? normalized = null;
            PortableGameIdentityV1? portable = null;
            if (kind == "uuid")
            {
                valid = LayoutContractV1Validator.TryNormalizeUuid(
                    testCase.GetProperty("input").GetString(),
                    out normalized!);
            }
            else
            {
                var input = testCase.GetProperty("input");
                valid = LayoutContractV1Validator.TryNormalizePortableIdentity(
                    new PortableGameIdentityV1(
                        input.GetProperty("provider").GetString()!,
                        input.GetProperty("id").GetString()!),
                    out portable!);
            }

            Assert.AreEqual(
                expected.GetProperty("valid").GetBoolean(),
                valid,
                testCase.GetProperty("id").GetString());
            if (!valid) continue;
            if (kind == "uuid")
            {
                Assert.AreEqual(expected.GetProperty("normalized").GetString(), normalized);
            }
            else
            {
                Assert.AreEqual(expected.GetProperty("provider").GetString(), portable!.Provider);
                Assert.AreEqual(expected.GetProperty("id").GetString(), portable.Id);
            }
        }
    }

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
