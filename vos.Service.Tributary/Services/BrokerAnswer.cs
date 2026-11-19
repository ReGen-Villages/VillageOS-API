namespace vos.Service.Tributary.Services;

public sealed record BrokerAnswer(bool Accepted, string? Refusal = null, bool CanBeTriedAgain = false, TimeSpan? Wait = null)
{
    public static readonly BrokerAnswer Done = new(true);
}
