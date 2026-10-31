namespace CardScout.Core.Billing;

// Which plan a user is on: "free" or "pro" (see Plans). The Stripe-backed implementation
// replaces FreePlanSubscriptionService; everything else only depends on this interface.
public interface ISubscriptionService
{
    Task<string> GetPlanAsync(long userId);
}

// Placeholder until billing is wired up: everyone is on the free plan
public class FreePlanSubscriptionService : ISubscriptionService
{
    public Task<string> GetPlanAsync(long userId) => Task.FromResult(Plans.Free);
}
