using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Services;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class PhloemReadsARunsSubjectTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public PhloemReadsARunsSubjectTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_subject_the_engine_holds_is_read_with_its_name_and_one_it_does_not_hold_is_none()
    {
        var name = $"Reservoir {Guid.NewGuid():N}";
        var held = await _engine.DeclareAsync(name);
        var gateway = new MyceliumGateway(_engine.ClientFactory, NullLogger<MyceliumGateway>.Instance, TheEngine.Url, _engine.AdminToken);

        (await gateway.SubjectAsync(held, CancellationToken.None)).Should().Be(new RunSubject(held, name));
        (await gateway.SubjectAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }
}
