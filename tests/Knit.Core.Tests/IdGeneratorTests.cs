using Knit.Core.Ids;

namespace Knit.Core.Tests;

public class IdGeneratorTests
{
    [Fact]
    public void New_ProducesValidNanoId()
    {
        var id = IdGenerator.New();
        Assert.True(IdGenerator.IsValid(id));
        Assert.Equal(NanoId.Length, id.Length);
    }

    [Fact]
    public void New_UsesOnlyTheUppercaseDigitAlphabet()
    {
        for (var i = 0; i < 500; i++)
        {
            var id = IdGenerator.New();
            Assert.All(id, c => Assert.Contains(c, NanoId.Alphabet));
        }
    }

    [Fact]
    public void New_IsCaseSafe_NoLowercaseLetters()
    {
        // Uppercase-only alphabet: never emit a-z, so case-insensitive
        // filesystems can't collide two distinct ids.
        for (var i = 0; i < 500; i++)
            Assert.DoesNotContain(IdGenerator.New(), c => c is >= 'a' and <= 'z');
    }

    [Fact]
    public void New_IsUniqueAcrossManyMints()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 10_000; i++)
            Assert.True(seen.Add(IdGenerator.New()));
    }
}

public class NanoIdTests
{
    [Fact]
    public void IsValid_AcceptsAReal18CharId()
    {
        Assert.True(NanoId.IsValid("ABCDEFGHIJ01234567"));
    }

    [Fact]
    public void IsValid_IsCaseFolded()
    {
        // A lowercase-typed id still validates (filesystems fold case).
        Assert.True(NanoId.IsValid("abcdefghij01234567"));
    }

    [Theory]
    [InlineData("foo")]                    // KB-ref leaf — too short
    [InlineData("hair-installation")]      // slug — hyphen + wrong length
    [InlineData("ABCDEFGHIJ0123456")]      // 17 chars
    [InlineData("ABCDEFGHIJ012345678")]    // 19 chars
    [InlineData("ABCDEFGHIJ_1234567")]     // illegal char
    [InlineData("")]
    public void IsValid_RejectsNonIds(string s)
    {
        Assert.False(NanoId.IsValid(s));
    }

    [Fact]
    public void IsValid_RejectsNull()
    {
        Assert.False(NanoId.IsValid(null));
    }
}
