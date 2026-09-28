using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class StringsTests
{
    private static string ResourcesDir()
    {
        // bin/<config>/<tfm>/ -> repo root -> SpaceSharp/Resources
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SpaceSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SpaceSharp", "Resources");
    }

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(file).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);

    [Fact]
    public void NeutralStringsLoad()
    {
        Assert.Equal("Language", Strings.Get("Settings_Language"));
        Assert.StartsWith("[", Strings.Get("No_Such_Key")); // a missing key is visible, never an exception
    }

    [Fact]
    public void EveryTranslationHasTheSameKeysAndPlaceholders()
    {
        string dir = ResourcesDir();
        var neutral = Read(Path.Combine(dir, "Strings.resx"));
        var placeholders = new Regex(@"\{\d+\}");
        foreach (string file in Directory.EnumerateFiles(dir, "Strings.*.resx"))
        {
            var translated = Read(file);
            string name = Path.GetFileName(file);
            var missing = neutral.Keys.Except(translated.Keys).ToList();
            var extra = translated.Keys.Except(neutral.Keys).ToList();
            Assert.True(missing.Count == 0, $"{name} is missing: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"{name} has keys the neutral file lacks: {string.Join(", ", extra)}");
            foreach (var (key, value) in neutral)
            {
                var want = placeholders.Matches(value).Select(m => m.Value).OrderBy(x => x).ToList();
                var got = placeholders.Matches(translated[key]).Select(m => m.Value).OrderBy(x => x).ToList();
                Assert.True(want.SequenceEqual(got), $"{name}: {key} should use placeholders {string.Join(" ", want)}");
            }
        }
    }

    [Fact]
    public void AvailableLanguagesStartWithEnglish()
    {
        var list = AppLanguages.Available();
        Assert.Equal("en", list[0].Name);
        Assert.Equal(list.Count, list.Select(c => c.Name).Distinct().Count());
    }
}
