namespace CardScout.Core.Models;

// A user's row in CARD_SUBSCRIPTIONS: their Stripe customer and current subscription
public class CardSubscription
{
    public long UserId { get; set; }
    public string StripeCustomerId { get; set; } = "";
    public string? StripeSubscriptionId { get; set; }
    public string? StripePriceId { get; set; }
    public string? BillingInterval { get; set; }
    public string Plan { get; set; } = "FREE";
    public string? Status { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public string CancelAtPeriodEnd { get; set; } = "N";
    public DateTime? LastPaymentFailedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// The subscription state written from Stripe (webhook or a fresh fetch)
public record SubscriptionSnapshot(
    string StripeCustomerId,
    string StripeSubscriptionId,
    string? PriceId,
    string? Interval,
    string Status,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);
