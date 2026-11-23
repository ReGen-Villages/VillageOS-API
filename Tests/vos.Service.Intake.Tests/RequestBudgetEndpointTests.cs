using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Intake.Tests;

public class RequestBudgetEndpointTests
{
    private const string Ana = "ana.ferreira@example.pt";
    private const string PerVerifiedTicket = "Request budget per verified ticket";
    private const string WithoutATicket = "Request budget without a ticket";

    private static readonly JsonSerializerOptions AsTheBrokerWritesIt = new(JsonSerializerDefaults.Web);

    private sealed class Broker
    {
        private int _budgetReads;

        public SnapshotDocument Model { get; set; } = WithBudgets(verified: 6, unverified: 4);

        public int BudgetReads => Volatile.Read(ref _budgetReads);

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            if (ModelStub.IsDeclarationRead(request)
                && request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains(LandIntakeRoles.VerifiedRequestBudget.Id.ToString()))
            {
                Interlocked.Increment(ref _budgetReads);
                return ModelStub.Json(JsonSerializer.Serialize(new SnapshotReadResult(0, Model), AsTheBrokerWritesIt));
            }
            return ModelStub.Holds(request);
        }
    }

    private static SnapshotDocument WithBudgets(long verified, long unverified, long seconds = 600) =>
        new DeclaredModel()
            .Stating(PerVerifiedTicket, (RequestBudgetReader.RequestCountProperty, verified), (RequestBudgetReader.WindowSecondsProperty, seconds))
            .Bound(LandIntakeRoles.VerifiedRequestBudget, PerVerifiedTicket)
            .Stating(WithoutATicket, (RequestBudgetReader.RequestCountProperty, unverified), (RequestBudgetReader.WindowSecondsProperty, seconds))
            .Bound(LandIntakeRoles.UnverifiedRequestBudget, WithoutATicket)
            .Build();

    private static IntakeWebApplicationFactory Reading(Broker broker) => new()
    {
        HandlerCallback = broker.Answer,
        ReadsRequestBudgets = true,
    };

    private static bool Names(string line, Role role) => Regex.IsMatch(line, $@"\b{role.Word}\b");

    private static RequestBudgets Held(IntakeWebApplicationFactory factory) => factory.Services.GetRequiredService<RequestBudgets>();

    private static Task BudgetsHeldAsync(IntakeWebApplicationFactory factory, int verified, int unverified) =>
        Settle.UntilAsync(
            () => Held(factory).Current.Verified.RequestCount == verified && Held(factory).Current.Unverified.RequestCount == unverified,
            $"the intake holds {verified} for a verified ticket and {unverified} without one");

    // Moved on repeatedly, because a single move could land before the refresher sets its next wait and wake nothing.
    private static Task ReadAgainAsync(IntakeWebApplicationFactory factory, Broker broker, int reads) =>
        Settle.UntilAsync(
            () =>
            {
                if (broker.BudgetReads >= reads) return true;
                factory.Clock.Advance(HeldDeclarations.HeldFor);
                return false;
            },
            $"the budgets have been read {reads} times");

    private static async Task<string> TicketFor(IntakeWebApplicationFactory factory, HttpClient client, string from)
    {
        using var asking = new HttpRequestMessage(HttpMethod.Post, "/submissions/verification") { Content = JsonContent.Create(new { emailAddress = Ana }) };
        asking.Headers.Add("X-Forwarded-For", from);
        (await client.SendAsync(asking)).EnsureSuccessStatusCode();

        using var exchanging = new HttpRequestMessage(HttpMethod.Post, "/submissions/ticket")
        {
            Content = JsonContent.Create(new { emailAddress = Ana, code = factory.Mailer.CodeSentTo(Ana) }),
        };
        exchanging.Headers.Add("X-Forwarded-For", from);
        var exchanged = await client.SendAsync(exchanging);
        exchanged.EnsureSuccessStatusCode();
        return (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static async Task<HttpStatusCode> FormAsync(HttpClient client, string from, string? ticket = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/submissions/form");
        request.Headers.Add("X-Forwarded-For", from);
        if (ticket is not null) request.Headers.Add(SubmissionTicket.HeaderName, ticket);
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<IReadOnlyList<HttpStatusCode>> FormsAsync(HttpClient client, int count, string from, string? ticket = null)
    {
        var answers = new List<HttpStatusCode>();
        for (var request = 0; request < count; request++)
            answers.Add(await FormAsync(client, from, ticket));
        return answers;
    }

    [Fact]
    public async Task A_ticketed_request_is_counted_against_its_address_and_leaves_its_sources_budget_alone()
    {
        var broker = new Broker();
        await using var factory = Reading(broker);
        using var client = factory.CreateClient();
        await BudgetsHeldAsync(factory, verified: 6, unverified: 4);
        var ticket = await TicketFor(factory, client, from: "203.0.113.5");

        (await FormsAsync(client, 6, "203.0.113.5", ticket)).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        (await FormAsync(client, "203.0.113.5", ticket)).Should().Be(HttpStatusCode.TooManyRequests);

        (await FormsAsync(client, 2, "203.0.113.5")).Should().AllBeEquivalentTo(HttpStatusCode.OK,
            "the code and the ticket spent two of the source's four, and nothing ticketed spent any more");
        (await FormAsync(client, "203.0.113.5")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_verified_address_keeps_its_count_from_another_source()
    {
        var broker = new Broker();
        await using var factory = Reading(broker);
        using var client = factory.CreateClient();
        await BudgetsHeldAsync(factory, verified: 6, unverified: 4);
        var ticket = await TicketFor(factory, client, from: "203.0.113.5");

        await FormsAsync(client, 6, "203.0.113.5", ticket);

        (await FormAsync(client, "198.51.100.7", ticket)).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_forged_or_expired_ticket_is_counted_against_its_source()
    {
        var broker = new Broker();
        await using var factory = Reading(broker);
        using var client = factory.CreateClient();
        await BudgetsHeldAsync(factory, verified: 6, unverified: 4);
        var ticket = await TicketFor(factory, client, from: "203.0.113.5");
        factory.Clock.Advance(SubmissionTicket.ValidFor + TimeSpan.FromSeconds(1));

        (await FormAsync(client, "203.0.113.5", ticket)).Should().Be(HttpStatusCode.OK);
        (await FormAsync(client, "203.0.113.5", "not-a-ticket")).Should().Be(HttpStatusCode.OK);

        (await FormAsync(client, "203.0.113.5")).Should().Be(HttpStatusCode.TooManyRequests,
            "the code, the ticket, the expired ticket and the forged one have spent the source's four");
    }

    [Fact]
    public async Task A_budget_changed_in_the_model_changes_the_limit_with_no_restart()
    {
        var broker = new Broker { Model = WithBudgets(verified: 2, unverified: 2) };
        await using var factory = Reading(broker);
        using var client = factory.CreateClient();
        await BudgetsHeldAsync(factory, verified: 2, unverified: 2);
        (await FormsAsync(client, 3, "203.0.113.5")).Last().Should().Be(HttpStatusCode.TooManyRequests);

        broker.Model = WithBudgets(verified: 5, unverified: 5);
        await ReadAgainAsync(factory, broker, broker.BudgetReads + 1);
        await BudgetsHeldAsync(factory, verified: 5, unverified: 5);

        (await FormsAsync(client, 5, "203.0.113.5")).Should().AllBeEquivalentTo(HttpStatusCode.OK,
            "the changed budget opens a window of its own for a source that had spent the old one");
    }

    [Fact]
    public async Task A_model_with_no_budgets_is_held_to_the_fallback_and_says_so_once()
    {
        var broker = new Broker { Model = new DeclaredModel().Build() };
        await using var factory = Reading(broker);
        using var client = factory.CreateClient();
        await ReadAgainAsync(factory, broker, 3);

        Held(factory).Current.Verified.Should().Be(RequestBudget.Fallback);
        Held(factory).Current.Unverified.Should().Be(RequestBudget.Fallback);
        factory.BudgetLog.Lines.Count(line => Names(line, LandIntakeRoles.VerifiedRequestBudget)).Should().Be(1);
        factory.BudgetLog.Lines.Count(line => Names(line, LandIntakeRoles.UnverifiedRequestBudget)).Should().Be(1);
    }

    [Fact]
    public async Task A_model_that_cannot_be_read_keeps_the_last_budgets_it_gave()
    {
        var broker = new Broker { Model = WithBudgets(verified: 6, unverified: 4) };
        await using var factory = Reading(broker);
        using var client = factory.CreateClient();
        await BudgetsHeldAsync(factory, verified: 6, unverified: 4);

        factory.HandlerCallback = request => ModelStub.IsDeclarationRead(request)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : broker.Answer(request);
        var failedBefore = factory.BudgetLog.Lines.Count;
        await Settle.UntilAsync(
            () =>
            {
                factory.Clock.Advance(HeldDeclarations.HeldFor);
                return factory.BudgetLog.Lines.Count > failedBefore;
            },
            "the failed read is logged");

        Held(factory).Current.Verified.RequestCount.Should().Be(6);
        Held(factory).Current.Unverified.RequestCount.Should().Be(4);
    }
}
