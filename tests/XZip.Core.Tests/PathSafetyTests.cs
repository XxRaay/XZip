using FluentAssertions;

using XZip.Core.Internal;
using XZip.Core.Tests.Helpers;

namespace XZip.Core.Tests;

public class PathSafetyTests
{
    [Fact]
    public void ResolveSafeDestination_NormalEntry_StaysInsideRoot()
    {
        using var tmp = new TempDir("safe");
        var resolved = PathSafety.ResolveSafeDestination(tmp.Path, "sub/file.txt");
        resolved.Should().StartWith(tmp.Path);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("sub/../../escape.txt")]
    [InlineData("/abs/escape.txt")] // absolute path joined into root must not escape
    public void ResolveSafeDestination_BlocksTraversal(string entry)
    {
        using var tmp = new TempDir("safe");
        var act = () => PathSafety.ResolveSafeDestination(tmp.Path, entry);

        // Either the path stays inside root (because absolute is treated relative)
        // or we throw. Both are acceptable; what is NOT acceptable is silently escaping.
        try
        {
            var resolved = act();
            resolved.Should().StartWith(tmp.Path);
        }
        catch (IOException)
        {
            // Expected for ../-style traversal.
        }
    }

    [Fact]
    public void GetUniquePath_AppendsCounter()
    {
        using var tmp = new TempDir("unique");
        var path = tmp.CreateFile("a.txt", "x");
        var unique = PathSafety.GetUniquePath(path);
        unique.Should().NotBe(path);
        unique.Should().Contain("(1)");
    }
}
