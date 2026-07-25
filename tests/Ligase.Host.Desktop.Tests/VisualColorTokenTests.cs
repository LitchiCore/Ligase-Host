using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class VisualColorTokenTests
{
    private static readonly (string MachineName, string ResourceName)[] Tokens =
    [
        ("brandPrimary", "BrandPrimary"),
        ("brandSecondary", "BrandSecondary"),
        ("background", "Background"),
        ("surface", "Surface"),
        ("surfaceVariant", "SurfaceVariant"),
        ("textPrimary", "TextPrimary"),
        ("textSecondary", "TextSecondary"),
        ("border", "Border"),
        ("selected", "Selected"),
        ("success", "Success"),
        ("warning", "Warning"),
        ("errorDanger", "ErrorDanger"),
        ("disabled", "Disabled"),
        ("focus", "Focus")
    ];

    [TestMethod]
    public void ThemeDictionary_MatchesMachineReadableAuthority()
    {
        var dictionaries = LoadThemeDictionaries();
        var authority = LoadAuthority();

        foreach (var theme in new[] { "Default", "Light", "Dark" })
        {
            Assert.IsTrue(dictionaries.TryGetValue(theme, out var colors), $"Missing {theme} theme.");
            var mode = theme == "Dark" ? "dark" : "light";
            Assert.AreEqual(Tokens.Length, authority[mode].Count, $"{mode} token count");
            Assert.AreEqual(Tokens.Length, colors.Count, $"{theme} token count");

            foreach (var (machineName, resourceName) in Tokens)
            {
                var key = $"Ligase{resourceName}Color";
                Assert.IsTrue(colors.TryGetValue(key, out var actual), $"{theme} is missing {resourceName}.");
                Assert.AreEqual(authority[mode][machineName], actual, $"{theme} {machineName}");
            }
        }
    }

    [TestMethod]
    public void DisabledEssentialTextMeetsContrastFloorOnEveryApprovedSurface()
    {
        var dictionaries = LoadThemeDictionaries();

        foreach (var theme in new[] { "Light", "Dark" })
        {
            var colors = dictionaries[theme];
            var disabled = colors["LigaseDisabledColor"];
            foreach (var surfaceName in new[] { "Surface", "Background", "SurfaceVariant", "Selected" })
            {
                Assert.IsTrue(
                    Contrast(disabled, colors[$"Ligase{surfaceName}Color"]) >= 4.5,
                    $"{theme} disabled on {surfaceName}");
            }
        }
    }

    [TestMethod]
    public void MeaningfulStateMappings_DoNotUseDecorativeBorderToken()
    {
        var aliases = LoadBrushAliases();

        foreach (var key in new[]
                 {
                     "FocusVisualPrimaryBrush",
                     "SystemControlFocusVisualPrimaryBrush",
                     "FocusStrokeColorOuterBrush"
                 })
        {
            Assert.AreEqual("{ThemeResource LigaseFocusColor}", aliases[key], key);
        }

        Assert.AreEqual(
            "{ThemeResource LigaseSelectedColor}",
            aliases["NavigationViewItemBackgroundSelected"]);
        Assert.AreEqual(
            "{ThemeResource LigaseTextPrimaryColor}",
            aliases["NavigationViewItemForegroundSelected"]);

        foreach (var key in new[] { "LigaseCardStrokeBrush", "LigaseDividerBrush", "LigaseGameTileStrokeBrush" })
        {
            Assert.AreEqual("{ThemeResource LigaseBorderColor}", aliases[key], key);
        }
    }

    [TestMethod]
    public void TitleBarBrandGlyph_UsesTopLevelThemeAwareBrushAlias()
    {
        var aliases = LoadBrushAliases();
        Assert.AreEqual(
            "{ThemeResource LigaseSurfaceColor}",
            aliases["LigaseOnBrandBrush"]);

        var mainWindow = File.ReadAllText(
            FindRepositoryFile("src", "Ligase.Desktop", "MainWindow.xaml"));
        StringAssert.Contains(
            mainWindow,
            "Foreground=\"{ThemeResource LigaseOnBrandBrush}\"");

        var styles = File.ReadAllText(
            FindRepositoryFile(
                "src",
                "Ligase.Desktop",
                "Themes",
                "Styles.xaml"));
        StringAssert.Contains(
            styles,
            "x:Key=\"LigasePrimaryButtonStyle\"");
        StringAssert.Contains(
            styles,
            "Value=\"{ThemeResource LigaseAccentBrush}\"");
        StringAssert.Contains(
            styles,
            "Value=\"{ThemeResource LigaseOnBrandBrush}\"");
        Assert.IsFalse(
            mainWindow.Contains(
                "Foreground=\"{ThemeResource LigaseSurfaceBrush}\"",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void RawApplicationColors_AreLimitedToDocumentedMediaOverlayException()
    {
        var desktopRoot = FindRepositoryDirectory("src", "Ligase.Desktop");
        var rawColor = new Regex(
            @"#[0-9A-Fa-f]{6,8}|(?:Foreground|Background)=""(?:White|Black|Red|Gray|Blue|Green)""",
            RegexOptions.CultureInvariant);

        var offenders = Directory.EnumerateFiles(desktopRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.EndsWith(
                Path.Combine("Themes", "Colors.xaml"),
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(
                Path.Combine("Pages", "StreamMonitorPage.xaml"),
                StringComparison.OrdinalIgnoreCase))
            .Where(path => rawColor.IsMatch(File.ReadAllText(path)))
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), offenders);
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

    private static Dictionary<string, string> LoadBrushAliases()
    {
        var path = FindRepositoryFile("src", "Ligase.Desktop", "Themes", "Colors.xaml");
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return document.Root!
            .Elements(presentation + "SolidColorBrush")
            .ToDictionary(
                brush => brush.Attribute(x + "Key")!.Value,
                brush => brush.Attribute("Color")!.Value);
    }

    private static Dictionary<string, Dictionary<string, string>> LoadAuthority()
    {
        var path = FindRepositoryFile("docs", "ligase-host", "visual-color-tokens-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
        CollectionAssert.AreEquivalent(
            new[] { "schemaVersion", "light", "dark" },
            root.EnumerateObject().Select(property => property.Name).ToArray());

        return new[] { "light", "dark" }.ToDictionary(
            mode => mode,
            mode => root.GetProperty(mode)
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.GetString()!,
                    StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        foreach (var root in RepositorySearchRoots())
        {
            var candidate = Path.Combine([root, .. relativeSegments]);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Unable to locate the Ligase Host repository.");
    }

    private static string FindRepositoryDirectory(params string[] relativeSegments)
    {
        foreach (var root in RepositorySearchRoots())
        {
            var candidate = Path.Combine([root, .. relativeSegments]);
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Unable to locate the Ligase Host repository.");
    }

    private static IEnumerable<string> RepositorySearchRoots()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }
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
