using CardScout.Core.Data;
using CardScout.Core.Models;
using Microsoft.Extensions.Logging;
using Stripe;

namespace CardScout.Core.Billing;

// Config section "Stripe". Keys live in user-secrets; price ids and URLs can go in either.
public class StripeOptions
{
    public string? SecretKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? PriceIdMonthly { get; set; }
    public string? PriceIdYearly { get; set; }
    // Optional overrides. Default: the API's own /billing/success and /billing/cancel pages,
    // which send the browser back into the app.
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
    public string? PortalReturnUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(PriceIdMonthly);
}

// An expected billing failure with the HTTP status the API should return
public class BillingException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public record WebhookResult(string EventId, string EventType, bool Duplicate, long? UserId);

// Subscriptions are paid on Stripe-hosted pages (Checkout, Billing Portal) in the browser, not
// through in-app purchase. CARD_SUBSCRIPTIONS mirrors Stripe and is updated from webhooks.
public class StripeBillingService
{
    private const string UserIdKey = "userId";

    private readonly StripeOptions _options;
    private readonly SubscriptionRepository _repo;
    private readonly ISubscriptionService _plans;
    private readonly ILogger<StripeBillingService> _logger;
    private readonly StripeClient? _client;

    public StripeBillingService(StripeOptions options, SubscriptionRepository repo, ISubscriptionService plans,
        ILogger<StripeBillingService> logger)
    {
        _options = options;
        _repo = repo;
        _plans = plans;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(options.SecretKey))
            _client = new StripeClient(options.SecretKey);
    }

    public bool IsConfigured => _options.IsConfigured;
    public bool HasYearly => !string.IsNullOrWhiteSpace(_options.PriceIdYearly);

    private StripeClient Client => _client ?? throw NotConfigured();

    private static BillingException NotConfigured() =>
        new(503, "Billing isn't set up on the server yet (Stripe:SecretKey / Stripe:PriceIdMonthly).");

    // Checkout Session (mode=subscription) for this user; returns the hosted page URL
    public async Task<string> CreateCheckoutAsync(long userId, string interval, string successUrl, string cancelUrl)
    {
        if (!IsConfigured) throw NotConfigured();

        var priceId = interval switch
        {
            "month" => _options.PriceIdMonthly,
            "year" when HasYearly => _options.PriceIdYearly,
            "year" => throw new BillingException(400, "Yearly billing isn't offered yet."),
            _ => throw new BillingException(400, "interval must be \"month\" or \"year\"."),
        };

        var existing = await _repo.GetAsync(userId);
        if (SubscriptionService.ToPlan(existing).IsPro)
            throw new BillingException(409, "You already have an active subscription. Use Manage subscription to change it.");

        var customerId = existing?.StripeCustomerId ?? await CreateCustomerAsync(userId);

        var options = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            ClientReferenceId = userId.ToString(),
            LineItems = [new Stripe.Checkout.SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            AllowPromotionCodes = true,
            Metadata = new Dictionary<string, string> { [UserIdKey] = userId.ToString() },
            SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [UserIdKey] = userId.ToString() },
            },
        };

        var sessions = new Stripe.Checkout.SessionService(Client);
        try
        {
            return (await sessions.CreateAsync(options)).Url;
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing" && ex.StripeError?.Param == "customer")
        {
            // The customer was deleted in Stripe (e.g. test data was cleared): start over with a new one
            _logger.LogWarning("Stripe customer {CustomerId} for user {UserId} is gone; creating a new one", customerId, userId);
            options.Customer = await CreateCustomerAsync(userId);
            return (await sessions.CreateAsync(options)).Url;
        }
    }

    private async Task<string> CreateCustomerAsync(long userId)
    {
        var (name, email) = await _repo.GetUserContactAsync(userId);
        var customer = await new CustomerService(Client).CreateAsync(new CustomerCreateOptions
        {
            Name = name,
            Email = email,
            Metadata = new Dictionary<string, string> { [UserIdKey] = userId.ToString() },
        });
        await _repo.SetCustomerAsync(userId, customer.Id);
        return customer.Id;
    }

    // Billing Portal session where the user can cancel, switch plans or update their card
    public async Task<string> CreatePortalAsync(long userId, string returnUrl)
    {
        if (_client == null) throw NotConfigured();
        var existing = await _repo.GetAsync(userId);
        if (existing == null)
            throw new BillingException(404, "No subscription yet. Upgrade first.");

        var session = await new Stripe.BillingPortal.SessionService(Client).CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions { Customer = existing.StripeCustomerId, ReturnUrl = returnUrl });
        return session.Url;
    }

    // Account deletion: deleting the Stripe customer cancels its subscriptions immediately, so a
    // deleted user is never charged again. No-op for users who never started checkout.
    public async Task DeleteCustomerAsync(long userId)
    {
        var existing = await _repo.GetAsync(userId);
        if (existing == null) return;
        if (_client == null)
            throw new BillingException(503, "Billing isn't configured, so the subscription can't be cancelled. Try again later.");
        try
        {
            await new CustomerService(Client).DeleteAsync(existing.StripeCustomerId);
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            // Already deleted in Stripe
        }
        _plans.Invalidate(userId);
    }

    // The success page calls this so the plan is current even before (or without) the webhook.
    // Only the user the session was created for is updated.
    public async Task<long?> SyncCheckoutSessionAsync(string sessionId)
    {
        if (_client == null) return null;
        var session = await new Stripe.Checkout.SessionService(Client).GetAsync(sessionId);
        return await HandleCheckoutCompletedAsync(session);
    }

    public async Task<WebhookResult> HandleWebhookAsync(string json, string? signature)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            throw new BillingException(503, "Stripe:WebhookSecret is not set.");

        // Throws StripeException when the signature doesn't match
        var stripeEvent = EventUtility.ConstructEvent(json, signature, _options.WebhookSecret,
            throwOnApiVersionMismatch: false);

        if (await _repo.IsEventProcessedAsync(stripeEvent.Id))
            return new WebhookResult(stripeEvent.Id, stripeEvent.Type, true, null);

        long? userId = null;
        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                if (stripeEvent.Data.Object is Stripe.Checkout.Session session)
                    userId = await HandleCheckoutCompletedAsync(session);
                break;

            case EventTypes.CustomerSubscriptionCreated:
            case EventTypes.CustomerSubscriptionUpdated:
            case EventTypes.CustomerSubscriptionDeleted:
                if (stripeEvent.Data.Object is Subscription sub)
                    userId = await SyncSubscriptionAsync(sub.Id, null);
                break;

            case EventTypes.InvoicePaymentFailed:
                if (stripeEvent.Data.Object is Invoice invoice)
                {
                    var subId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
                    userId = subId != null
                        ? await SyncSubscriptionAsync(subId, null)
                        : invoice.CustomerId != null ? await _repo.FindUserIdByCustomerAsync(invoice.CustomerId) : null;
                    if (userId != null)
                    {
                        await _repo.MarkPaymentFailedAsync(userId.Value);
                        _logger.LogWarning("Stripe payment failed for user {UserId} (invoice {InvoiceId})", userId, invoice.Id);
                    }
                }
                break;

            default:
                _logger.LogDebug("Ignoring Stripe event {Type}", stripeEvent.Type);
                break;
        }

        await _repo.MarkEventProcessedAsync(stripeEvent.Id, stripeEvent.Type, userId);
        return new WebhookResult(stripeEvent.Id, stripeEvent.Type, false, userId);
    }

    private async Task<long?> HandleCheckoutCompletedAsync(Stripe.Checkout.Session session)
    {
        if (session.Mode != "subscription" || session.SubscriptionId == null) return null;
        long? hint = long.TryParse(session.ClientReferenceId, out var id) ? id : null;
        return await SyncSubscriptionAsync(session.SubscriptionId, hint);
    }

    // Events can arrive out of order, so rather than trusting the payload this re-reads the
    // subscription from Stripe and stores its current state.
    private async Task<long?> SyncSubscriptionAsync(string subscriptionId, long? userIdHint)
    {
        var sub = await new Stripe.SubscriptionService(Client).GetAsync(subscriptionId);

        var userId = userIdHint
            ?? (sub.Metadata != null && sub.Metadata.TryGetValue(UserIdKey, out var raw) && long.TryParse(raw, out var parsed)
                ? parsed
                : await _repo.FindUserIdByCustomerAsync(sub.CustomerId));

        if (userId == null)
        {
            _logger.LogWarning("Stripe subscription {SubscriptionId} (customer {CustomerId}) has no CardScout user; ignored",
                sub.Id, sub.CustomerId);
            return null;
        }

        var item = sub.Items?.Data?.FirstOrDefault();
        var snapshot = new SubscriptionSnapshot(
            sub.CustomerId,
            sub.Id,
            item?.Price?.Id,
            item?.Price?.Recurring?.Interval,
            sub.Status,
            item?.CurrentPeriodEnd,
            // The portal can cancel either "at period end" or with an explicit cancel_at date
            sub.CancelAtPeriodEnd || sub.CancelAt != null);

        await _repo.UpsertSubscriptionAsync(userId.Value, snapshot);
        _plans.Invalidate(userId.Value);
        _logger.LogInformation("Subscription {SubscriptionId} for user {UserId} is {Status}", sub.Id, userId, sub.Status);
        return userId;
    }
}
