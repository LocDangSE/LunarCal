using System.Net;
using LunarCal.Infrastructure.Google.OAuth;

namespace LunarCal.Infrastructure.Tests.Google;

public sealed class LoopbackHttpListenerTests
{
    [Fact]
    public void SEC_OAUTH_1_Loopback_listener_binds_only_to_127_0_0_1_on_dynamic_port()
    {
        using var listener = new LoopbackHttpListener();

        listener.RedirectUri.Should().StartWith("http://127.0.0.1:");
        listener.RedirectUri.Should().EndWith("/");
        listener.Port.Should().BeGreaterThan(0);
        listener.Port.Should().BeLessThanOrEqualTo(65535);
        listener.IsListening.Should().BeFalse();

        listener.Start();
        listener.IsListening.Should().BeTrue();

        listener.StopListening();
        listener.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task SEC_OAUTH_1_Loopback_listener_closes_immediately_after_callback()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new LoopbackHttpListener();
        listener.Start();

        var waitTask = listener.WaitForCallbackAsync(ct);

        using var client = new HttpClient();
        var callbackUrl = $"{listener.RedirectUri}?code=auth_code_sample&state=test_state_123";

        var response = await client.GetAsync(callbackUrl, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(ct);
        body.Should().Contain("Đăng nhập thành công");
        body.Should().Contain("LunarCal");

        var result = await waitTask;
        result.Code.Should().Be("auth_code_sample");
        result.State.Should().Be("test_state_123");
        result.Error.Should().BeNull();

        // SEC-OAUTH-1: Listener must be stopped immediately after callback
        listener.IsListening.Should().BeFalse();

        // Connecting again should fail because the listener is already closed
        var secondRequestAct = async () => await client.GetAsync(callbackUrl, ct);
        await secondRequestAct.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SEC_OAUTH_1_Loopback_listener_closes_after_timeout()
    {
        // 200 ms timeout for fast test execution
        var shortTimeout = TimeSpan.FromMilliseconds(200);
        using var listener = new LoopbackHttpListener(timeout: shortTimeout);

        var act = async () => await listener.WaitForCallbackAsync();

        await act.Should().ThrowAsync<TimeoutException>();

        // SEC-OAUTH-1: Listener must be closed after timeout
        listener.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task FR_AUTH_1_Loopback_listener_returns_error_callback_when_error_parameter_present()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new LoopbackHttpListener();

        var waitTask = listener.WaitForCallbackAsync(ct);

        using var client = new HttpClient();
        var callbackUrl = $"{listener.RedirectUri}?error=access_denied&error_description=User%20denied%20consent";

        var response = await client.GetAsync(callbackUrl, ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await waitTask;
        result.Error.Should().Be("access_denied");
        result.ErrorDescription.Should().Be("User denied consent");
        result.Code.Should().BeNull();

        listener.IsListening.Should().BeFalse();
    }
}
