using LunarCal.Infrastructure.Google.OAuth;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class PkceParametersTests
{
    [Fact]
    public void SEC_OAUTH_1_Pkce_parameters_generate_valid_rfc7636_verifier_and_challenge()
    {
        var pkce = PkceParameters.Create();

        PkceParameters.CodeChallengeMethod.Should().Be("S256");
        pkce.CodeVerifier.Length.Should().BeInRange(43, 128);

        // All characters must be RFC 7636 unreserved characters
        foreach (var c in pkce.CodeVerifier)
        {
            var isUnreserved = char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';
            isUnreserved.Should().BeTrue($"verifier contains character '{c}'");
        }

        // Challenge must match recalculated S256 challenge
        var expectedChallenge = PkceParameters.ComputeChallenge(pkce.CodeVerifier);
        pkce.CodeChallenge.Should().Be(expectedChallenge);
        pkce.CodeChallenge.Should().NotContain("=");
        pkce.CodeChallenge.Should().NotContain("+");
        pkce.CodeChallenge.Should().NotContain("/");
    }

    [Fact]
    public void SEC_OAUTH_1_Pkce_parameters_matches_known_rfc7636_test_vector()
    {
        // RFC 7636 Appendix B test vector
        const string testVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = PkceParameters.ComputeChallenge(testVerifier);

        challenge.Should().Be(expectedChallenge);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("123456789012345678901234567890123456789012")] // 42 chars
    public void SEC_OAUTH_1_Pkce_rejects_verifier_shorter_than_43_chars(string shortVerifier)
    {
        var act = () => PkceParameters.ValidateVerifier(shortVerifier);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SEC_OAUTH_1_Pkce_rejects_verifier_longer_than_128_chars()
    {
        var longVerifier = new string('a', 129);
        var act = () => PkceParameters.ValidateVerifier(longVerifier);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk+invalid")] // contains '+'
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk/invalid")] // contains '/'
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk=invalid")] // contains '='
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r wW1gFWFOEjXk_invalid")] // contains space
    public void SEC_OAUTH_1_Pkce_rejects_invalid_characters_in_verifier(string invalidVerifier)
    {
        var act = () => PkceParameters.ValidateVerifier(invalidVerifier);
        act.Should().Throw<ArgumentException>();
    }
}
