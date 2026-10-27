using ComputerSetupWizard.Core;

namespace ComputerSetupWizard.Core.Tests;

public class ClaudeMdServiceTests
{
    static readonly string Section = ClaudeMdService.BuildSection(@"C:\Tools\ComputerSetupWizard.exe");

    [Fact]
    public void Merge_AppendsToAnExistingFile()
    {
        var merged = ClaudeMdService.Merge("# Other\n\nKeep this.\n", Section);

        Assert.StartsWith("# Other\n\nKeep this.\n\n" + ClaudeMdService.Heading, merged);
        Assert.EndsWith("normal Git identity.\n", merged);
    }

    [Fact]
    public void Merge_CreatesANewFile() =>
        Assert.Equal(Section.ReplaceLineEndings("\n") + "\n", ClaudeMdService.Merge("", Section));

    [Fact]
    public void Merge_ReplacesTheOldSectionAndKeepsTheRest()
    {
        var existing = string.Join("\n",
            "# Before", "", "text", "",
            ClaudeMdService.Heading, "", "- old instructions", "  - old nested", "",
            "# After", "", "more text", "");

        var merged = ClaudeMdService.Merge(existing, Section);

        Assert.DoesNotContain("old instructions", merged);
        Assert.StartsWith("# Before\n\ntext\n\n" + ClaudeMdService.Heading, merged);
        Assert.EndsWith("normal Git identity.\n\n# After\n\nmore text\n", merged);
    }

    [Fact]
    public void Merge_IsStable()
    {
        var once = ClaudeMdService.Merge("# Other\r\n\r\ntext\r\n", Section);
        Assert.Equal(once, ClaudeMdService.Merge(once, Section));
        Assert.DoesNotContain("\n", once.Replace("\r\n", ""));
    }
}
