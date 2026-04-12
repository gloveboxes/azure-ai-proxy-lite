using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace AzureAIProxy.Tests.Security;

/// <summary>
/// Tests for the constant-time password comparison used in admin local login.
/// Validates that FixedTimeEquals correctly compares inputs (match, mismatch,
/// different lengths, empty strings) — the same logic used in Login.cshtml.cs.
/// </summary>
public class FixedTimePasswordCompareTests
{
    /// <summary>
    /// Mirrors the FixedTimeEquals helper added to Login.cshtml.cs.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    [Fact]
    public void IdenticalStrings_ReturnsTrue()
    {
        Assert.True(FixedTimeEquals("correct-password", "correct-password"));
    }

    [Fact]
    public void DifferentStrings_ReturnsFalse()
    {
        Assert.False(FixedTimeEquals("correct-password", "wrong-password"));
    }

    [Fact]
    public void DifferentLengths_ReturnsFalse()
    {
        Assert.False(FixedTimeEquals("short", "much-longer-password"));
    }

    [Fact]
    public void EmptyStrings_ReturnsTrue()
    {
        Assert.True(FixedTimeEquals("", ""));
    }

    [Fact]
    public void OneEmpty_ReturnsFalse()
    {
        Assert.False(FixedTimeEquals("", "notempty"));
        Assert.False(FixedTimeEquals("notempty", ""));
    }

    [Fact]
    public void CaseSensitive_DifferentCase_ReturnsFalse()
    {
        Assert.False(FixedTimeEquals("Password", "password"));
    }

    [Fact]
    public void Unicode_IdenticalStrings_ReturnsTrue()
    {
        Assert.True(FixedTimeEquals("pässwörd-über", "pässwörd-über"));
    }

    [Fact]
    public void OffByOneCharacter_ReturnsFalse()
    {
        Assert.False(FixedTimeEquals("password1", "password2"));
    }
}
