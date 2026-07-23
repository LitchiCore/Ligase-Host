using System.Globalization;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class VisualColorTokenTests
{
    private static readonly string[] TokenNames =
    [
        "BrandPrimary", "BrandSecondary", "Background", "Surface",
        "SurfaceVariant", "TextPrimary", "TextSecondary", "Border", "Selected",
        "Success", "Warning", "ErrorDanger", "Disabled", "Focus"
    ];

    [TestMethod]
    public void ThemeDictionary_DefinesFrozenTokensForEveryTheme()
    {
        var dictionaries = LoadThemeDictionaries();

        foreach (var theme in new[] { "Default", "Light", "Dark" })
        {
            Assert.IsTrue(dictionaries.TryGetValue(theme, out var colors), $"Missing {theme} theme.");
            foreach (var token in TokenNames)
            {
                Assert.IsTrue(colors.ContainsKey($"Ligase{token}Color"), $"{theme} is missing {token}.");
            }
        }
    }

    [TestMethod]
    public void ThemeDictionary_MatchesFrozenColorValues()
    {
        var dictionaries = LoadThemeDictionaries();
        var light = dictionaries["Light"];
        var dark = dictionaries["Dark"];

        Assert.AreEqual("#6258D9", light["LigaseBrandPrimaryColor"]);
        Assert.AreEqual("#E7E5FF", light["LigaseSelectedColor"]);
        Assert.AreEqual("#6B7382", light["LigaseDisabledColor"]);
        Assert.AreEqual("#B8B1FF", dark["LigaseBrandPrimaryColor"]);
        Assert.AreEqual("#35315C", dark["LigaseSelectedColor"]);
        Assert.AreEqual("#8D96A6", dark["LigaseDisabledColor"]);
    }

    [TestMethod]
    public void ThemeDictionary_EssentialTextMeetsContrastFloor()
    {
        var dictionaries = LoadThemeDictionaries();

        foreach (var theme in new[] { "Light", "Dark" })
        {
            var colors = dictionaries[theme];
            var surface = colors["LigaseSurfaceColor"];
            Assert.IsTrue(Contrast(colors["LigaseTextPrimaryColor"], surface) >= 4.5, $"{theme} textPrimary");
            Assert.IsTrue(Contrast(colors["LigaseTextSecondaryColor"], surface) >= 4.5, $"{theme} textSecondary");
            Assert.IsTrue(Contrast(colors["LigaseDisabledColor"], surface) >= 4.5, $"{theme} disabled");
            Assert.IsTrue(
                Contrast(colors["LigaseTextPrimaryColor"], colors["LigaseSelectedColor"]) >= 4.5,
                $"{theme} selected foreground");
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadThemeDictionaries()
    {
        var path = FindRepositoryFile("src", "Ligase.Desktop", "Themes", "Colors.xaml");
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return document
            .Descendants(presentation + "ResourceDictionary")
            .Where(element => element.Attribute(x + "Key") is not null)
            .ToDictionary(
                element => element.Attribute(x + "Key")!.Value,
                element => element.Elements(presentation + "Color")
                    .ToDictionary(
                        color => color.Attribute(x + "Key")!.Value,
                        color => color.Value.Trim()));
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate the Ligase Host repository.");
    }

    private static double Contrast(string first, string second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        var value = hex.TrimStart('#');
        var channels = Enumerable.Range(0, 3)
            .Select(index => int.Parse(value.Substring(index * 2, 2), NumberStyles.HexNumber) / 255d)
            .Select(channel => channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4))
            .ToArray();
        return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    }
}
