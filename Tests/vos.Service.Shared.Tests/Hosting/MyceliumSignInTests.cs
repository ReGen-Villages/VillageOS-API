using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using vos.Service.Shared.Hosting;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.Hosting;

public class MyceliumSignInTests
{
    private static readonly string VerificationKey = new MyceliumSigner().VerificationKey;

    private sealed class CollectingSink : ILogEventSink
    {
        public List<string> Messages { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            lock (Messages) Messages.Add(logEvent.RenderMessage());
        }
    }

    private static WebApplicationBuilder NewBuilder() => WebApplication.CreateBuilder(Array.Empty<string>());

    private static bool SignsIn(WebApplicationBuilder builder) =>
        builder.Services.Any(service => service.ServiceType == typeof(IAuthenticationService));

    [Fact]
    public void A_service_started_without_a_verification_key_turns_no_sign_in_on()
    {
        var builder = NewBuilder();

        builder.AddMyceliumTokenAuthWhenKeyed(verificationKey: null, "VillageOS", "Delta").Should().BeFalse();
        builder.AddMyceliumTokenAuthWhenKeyed(verificationKey: "", "VillageOS", "Delta").Should().BeFalse();
        SignsIn(builder).Should().BeFalse();
    }

    [Fact]
    public void A_service_started_with_a_verification_key_turns_sign_in_on_and_says_so_with_its_issuer_and_audience()
    {
        var sink = new CollectingSink();
        var before = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var builder = NewBuilder();

            builder.AddMyceliumTokenAuthWhenKeyed(VerificationKey, "VillageOS", "Delta").Should().BeTrue();

            SignsIn(builder).Should().BeTrue();
            sink.Messages.Should().Contain(
                "JWT authentication enabled for incoming mycelium requests (issuer=\"VillageOS\", audience=\"Delta\")");
        }
        finally
        {
            Log.Logger = before;
        }
    }
}
