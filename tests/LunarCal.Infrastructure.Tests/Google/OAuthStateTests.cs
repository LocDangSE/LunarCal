using LunarCal.Infrastructure.Google.OAuth;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class OAuthStateTests
{
    [Fact]
    public void SEC_OAUTH_1_OAuth_state_generates_cryptographic_random_string()
    {
        var state1 = OAuthState.Generate();
        var state2 = OAuthState.Generate();

        state1.Should().NotBeNullOrWhiteSpace();
        state2.Should().NotBeNullOrWhiteSpace();
        state1.Should().NotBe(state2);
        state1.Length.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void SEC_OAUTH_1_OAuth_state_validation_succeeds_for_matching_strings()
    {
        var state = OAuthState.Generate();
        OAuthState.Validate(state, state).Should().BeTrue();
    }

    [Theory]
    [InlineData("state-abc", "state-xyz")]
    [InlineData("state-abc", "")]
    [InlineData("state-abc", null)]
    [InlineData("", "state-abc")]
    [InlineData(null, "state-abc")]
    [InlineData("state-abc", "state-ab")]
    [InlineData("state-abc", "state-abcd")]
    public void SEC_OAUTH_1_OAuth_state_validation_fails_for_mismatched_or_empty(string? expected, string? received)
    {
        OAuthState.Validate(expected!, received).Should().BeFalse();
    }
}
