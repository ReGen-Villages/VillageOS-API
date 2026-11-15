using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The console's call settings panel reads the platform's answer for a connection and writes a change of choice as one
// fragment, so both are proved against the engine that serves them rather than against a stand-in.
public class TheConsoleReadsAndChoosesAConnectionsCallSettingsTests : IClassFixture<TheEngine>
{
    private static readonly string[] SettingsInOrder =
        ["completesWhen", "callsAgainWhileItsThingStaysIn", "doesNotCallAgainForSeconds", "callsAgainForNoLongerThanSeconds", "callsAgainAtMost"];

    private static Role RoleOf(string word) => new(word, RoleIdentity.Of(word));

    private readonly TheEngine _engine;

    public TheConsoleReadsAndChoosesAConnectionsCallSettingsTests(TheEngine engine) => _engine = engine;

    private sealed record Vocabulary(
        Guid Has, Guid CompletesWhen, Guid ConnectionArchetype, Guid ServiceArchetype,
        Guid WhenTheServiceAnswers, Guid WhenTheThingReachesTheDoneState, Guid WhenTheServiceReportsCompletion);

    private async Task<Guid> BindOnceAsync(Role role, Func<Task<Guid>> declare)
    {
        var binds = await _engine.DeclareOnceAsync("binds");
        if (await _engine.FindAsync($"{role.Word} role") is not null)
            return await _engine.TheOnlyThingNamedAsync($"{role.Word} bound");
        var played = await declare();
        await _engine.DeclareAsync($"{role.Word} role", identifier: role.Id);
        await _engine.RelateAsync(role.Id, binds, played);
        return played;
    }

    private Task<Guid> BoundArchetypeAsync(string word, IReadOnlyDictionary<string, object?>? properties = null) =>
        BindOnceAsync(RoleOf(word), () => _engine.DeclareAsync($"{word} bound", isArchetype: true, properties));

    private Task<Guid> BoundThingAsync(string word) =>
        BindOnceAsync(RoleOf(word), () => _engine.DeclareAsync($"{word} bound"));

    private async Task<Vocabulary> TheCallCompletionVocabularyAsync()
    {
        var isPredicate = await _engine.DeclareOnceAsync("is");
        var has = await _engine.DeclareOnceAsync("has");
        var completesWhen = await BoundThingAsync("completesWhen");
        var callCompletion = await BoundArchetypeAsync("callCompletion");
        var members = new List<Guid>();
        foreach (var word in new[] { "whenTheServiceAnswers", "whenTheThingReachesTheDoneState", "whenTheServiceReportsCompletion" })
        {
            var bound = await _engine.FindAsync($"{word} role") is not null;
            var member = await BoundThingAsync(word);
            if (!bound) await _engine.RelateAsync(member, isPredicate, callCompletion);
            members.Add(member);
        }

        var connectionsDeclared = await _engine.FindAsync("connection role") is not null;
        var connectionArchetype = await BoundArchetypeAsync("connection");
        if (!connectionsDeclared) await _engine.RelateAsync(connectionArchetype, completesWhen, callCompletion);
        var serviceArchetype = await BoundArchetypeAsync("service");
        return new Vocabulary(has, completesWhen, connectionArchetype, serviceArchetype, members[0], members[1], members[2]);
    }

    // Estimate-Rainfall chooses nothing; rainfall-estimator's own archetype finishes its calls on a report and allows
    // fifteen minutes of calling again.
    private async Task<(Guid Connection, Guid ServiceArchetype)> AConnectionWhoseServiceArchetypeChoosesAsync(Vocabulary vocabulary)
    {
        var isPredicate = await _engine.TheOnlyThingNamedAsync("is");
        var prototype = await _engine.DeclareAsync($"rainfall-estimator prototype {Guid.NewGuid():N}", isArchetype: true,
            new Dictionary<string, object?> { ["callsAgainForNoLongerThanSeconds"] = 900 });
        await _engine.RelateAsync(prototype, isPredicate, vocabulary.ServiceArchetype);
        await _engine.RelateAsync(prototype, vocabulary.CompletesWhen, vocabulary.WhenTheServiceReportsCompletion);
        var service = await _engine.DeclareAsync($"rainfall-estimator {Guid.NewGuid():N}");
        await _engine.RelateAsync(service, isPredicate, prototype);
        var connection = await _engine.DeclareAsync($"Estimate-Rainfall {Guid.NewGuid():N}");
        await _engine.RelateAsync(connection, isPredicate, vocabulary.ConnectionArchetype);
        await _engine.RelateAsync(connection, vocabulary.Has, service);
        return (connection, prototype);
    }

    private async Task<JsonElement> CallSettingsAsync(Guid connection)
    {
        var answer = await _engine.Admin.GetAsync($"/api/connections/{connection}/call-settings");
        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        return await answer.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task ApplyAsync(object fragment)
    {
        var applied = await _engine.Admin.PostAsJsonAsync("/api/model/fragment", fragment);
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
    }

    private async Task<string[]> ChoicesOnAsync(Guid connection, Guid completesWhen)
    {
        var relationships = await _engine.Admin.GetFromJsonAsync<JsonElement>("/api/relationships");
        return relationships.EnumerateArray()
            .Where(relationship => relationship.GetProperty("SubjectId").GetGuid() == connection
                                   && relationship.GetProperty("PredicateId").GetGuid() == completesWhen)
            .Select(relationship => relationship.GetProperty("Id").GetString()!)
            .ToArray();
    }

    [FactAgainstTheEngine]
    public async Task The_answer_names_every_setting_in_order_with_its_value_its_source_and_the_Thing_it_comes_from()
    {
        var vocabulary = await TheCallCompletionVocabularyAsync();
        var (connection, prototype) = await AConnectionWhoseServiceArchetypeChoosesAsync(vocabulary);

        var settings = await CallSettingsAsync(connection);

        settings.EnumerateObject().Select(setting => setting.Name).Should().Equal(SettingsInOrder);
        var completesWhen = settings.GetProperty("completesWhen");
        completesWhen.GetProperty("value").GetGuid().Should().Be(vocabulary.WhenTheServiceReportsCompletion);
        completesWhen.GetProperty("source").GetString().Should().Be("serviceArchetype");
        completesWhen.GetProperty("from").GetProperty("id").GetGuid().Should().Be(prototype);
        completesWhen.GetProperty("from").GetProperty("name").GetString().Should().StartWith("rainfall-estimator prototype");
        var timeLimit = settings.GetProperty("callsAgainForNoLongerThanSeconds");
        timeLimit.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Number);
        timeLimit.GetProperty("value").GetInt32().Should().Be(900);
        timeLimit.GetProperty("source").GetString().Should().Be("serviceArchetype");
        var callsAgain = settings.GetProperty("callsAgainWhileItsThingStaysIn");
        callsAgain.GetProperty("value").ValueKind.Should().Be(JsonValueKind.True);
        callsAgain.GetProperty("source").GetString().Should().Be("builtIn");
        callsAgain.GetProperty("from").ValueKind.Should().Be(JsonValueKind.Null);
        settings.GetProperty("doesNotCallAgainForSeconds").GetProperty("value").GetInt32().Should().Be(10);
        settings.GetProperty("callsAgainAtMost").GetProperty("value").GetInt32().Should().Be(5);
    }

    [FactAgainstTheEngine]
    public async Task A_choice_written_as_the_panel_writes_it_replaces_the_connections_own_choice_in_one_fragment()
    {
        var vocabulary = await TheCallCompletionVocabularyAsync();
        var (connection, _) = await AConnectionWhoseServiceArchetypeChoosesAsync(vocabulary);

        await ApplyAsync(new
        {
            Name = "Estimate-Rainfall completesWhen whenTheServiceAnswers",
            Relationships = new[] { new { Subject = connection, Predicate = vocabulary.CompletesWhen, Target = vocabulary.WhenTheServiceAnswers } },
        });
        var first = await CallSettingsAsync(connection);
        var ownChoice = (await ChoicesOnAsync(connection, vocabulary.CompletesWhen)).Should().ContainSingle().Subject;
        await ApplyAsync(new
        {
            Name = "Estimate-Rainfall completesWhen whenTheThingReachesTheDoneState",
            Retractions = new[] { new { Id = ownChoice } },
            Relationships = new[] { new { Subject = connection, Predicate = vocabulary.CompletesWhen, Target = vocabulary.WhenTheThingReachesTheDoneState } },
        });
        var second = await CallSettingsAsync(connection);

        first.GetProperty("completesWhen").GetProperty("value").GetGuid().Should().Be(vocabulary.WhenTheServiceAnswers);
        first.GetProperty("completesWhen").GetProperty("source").GetString().Should().Be("connection");
        first.GetProperty("completesWhen").GetProperty("from").GetProperty("id").GetGuid().Should().Be(connection);
        second.GetProperty("completesWhen").GetProperty("value").GetGuid().Should().Be(vocabulary.WhenTheThingReachesTheDoneState);
        (await ChoicesOnAsync(connection, vocabulary.CompletesWhen)).Should().ContainSingle().Which.Should().NotBe(ownChoice);
    }

    [FactAgainstTheEngine]
    public async Task A_second_choice_written_beside_the_first_is_refused_with_a_plain_sentence()
    {
        var vocabulary = await TheCallCompletionVocabularyAsync();
        var (connection, _) = await AConnectionWhoseServiceArchetypeChoosesAsync(vocabulary);
        await _engine.RelateAsync(connection, vocabulary.CompletesWhen, vocabulary.WhenTheServiceAnswers);

        var refused = await _engine.Admin.PostAsJsonAsync("/api/model/fragment", new
        {
            Name = "Estimate-Rainfall completesWhen whenTheThingReachesTheDoneState",
            Relationships = new[] { new { Subject = connection, Predicate = vocabulary.CompletesWhen, Target = vocabulary.WhenTheThingReachesTheDoneState } },
        });

        refused.IsSuccessStatusCode.Should().BeFalse();
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [FactAgainstTheEngine]
    public async Task An_identifier_that_names_no_connection_answers_404_with_a_sentence()
    {
        var vocabulary = await TheCallCompletionVocabularyAsync();

        foreach (var identifier in new[] { Guid.NewGuid(), vocabulary.WhenTheServiceAnswers })
        {
            var answer = await _engine.Admin.GetAsync($"/api/connections/{identifier}/call-settings");

            answer.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
