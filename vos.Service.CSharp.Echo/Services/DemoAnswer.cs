using vos.Service.Shared.Subscriptions;

namespace vos.Service.CSharp.Echo.Services;

// What a demo route answers. A subscription the platform refused is answered as the platform gave it. A
// call the platform did not answer in time, or one the service could not make for want of a credential,
// is answered with its reason, so the person trying the example sees why.
public static class DemoAnswer
{
    public static async Task<IResult> OfAsync<T>(Func<Task<T>> demo)
    {
        try
        {
            return Results.Ok(await demo());
        }
        catch (SubscriptionRefusedException refused)
        {
            return Results.Content(refused.Body, "application/json", statusCode: (int)refused.StatusCode!);
        }
        catch (Exception reason) when (reason is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return Results.Json(new { error = reason.Message }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
