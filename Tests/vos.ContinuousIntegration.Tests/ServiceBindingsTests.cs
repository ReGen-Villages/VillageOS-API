using System.Linq;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

// The sweep over the repository can only report which file is wrong; these say what "wrong" means, on
// source written out here rather than on whichever services happen to exist.
public class ServiceBindingsTests
{
    [Theory]
    [InlineData("builder.WebHost.UseUrls($\"http://localhost:{servicePort}\");")]
    [InlineData("builder.WebHost.UseUrls(\"http://localhost:\" + servicePort);")]
    [InlineData("builder.WebHost.UseUrls($\"http://127.0.0.1:{servicePort}\");")]
    [InlineData("builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(servicePort));")]
    public void A_loopback_binding_is_read_as_loopback_however_it_is_written(string source)
    {
        var call = Assert.Single(ServiceBindings.BindingCallsIn(source));

        Assert.True(ServiceBindings.IsLoopback(call), $"read as reaching past the machine: {call}");
    }

    [Theory]
    [InlineData("builder.WebHost.UseUrls($\"http://*:{servicePort}\");")]
    [InlineData("builder.WebHost.UseUrls($\"http://+:{servicePort}\");")]
    [InlineData("builder.WebHost.UseUrls(\"http://0.0.0.0:5000\");")]
    [InlineData("builder.WebHost.UseUrls(\"http://[::]:5000\");")]
    [InlineData("builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(servicePort));")]
    [InlineData("builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Any, servicePort));")]
    public void A_binding_reaching_past_the_machine_is_not_loopback(string source)
    {
        var calls = ServiceBindings.BindingCallsIn(source);

        Assert.NotEmpty(calls);
        Assert.DoesNotContain(calls, ServiceBindings.IsLoopback);
        Assert.True(ServiceBindings.ReachesBeyondLoopback(source));
    }

    // The bug this guard was rewritten for: a loopback address and a wildcard in the same call read as
    // loopback, and the service was on the network with the test green.
    [Fact]
    public void A_loopback_address_alongside_a_wildcard_is_not_loopback()
    {
        const string source = "builder.WebHost.UseUrls($\"http://localhost:{port}\", $\"http://*:{port}\");";

        var call = Assert.Single(ServiceBindings.BindingCallsIn(source));

        Assert.False(ServiceBindings.IsLoopback(call));
    }

    [Fact]
    public void The_loopback_form_of_an_ipv6_address_is_not_read_as_every_interface()
    {
        Assert.False(ServiceBindings.ReachesBeyondLoopback("builder.WebHost.UseUrls(\"http://[::1]:5000\");"));
    }

    [Fact]
    public void A_binding_call_is_read_to_its_own_closing_bracket_and_no_further()
    {
        const string source = """
            builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));
            app.MapGet("/health", () => new { status = "Healthy" });
            """;

        Assert.Equal("ListenLocalhost(port)", Assert.Single(ServiceBindings.BindingCallsIn(source)));
    }

    [Fact]
    public void An_entry_point_that_binds_nothing_states_no_binding_call()
    {
        Assert.Empty(ServiceBindings.BindingCallsIn("var app = builder.Build();\napp.Run();"));
    }
}
