using System.Net;
using System.Text.Json;
using CardScout.Core.Billing;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace CardScout.Api.Endpoints;

public record CheckoutRequest(string? Interval, string? ReturnUrl);
public record PortalRequest(string? ReturnUrl);
public record BillingUrlResponse(string Url);
public record BillingStatusResponse(
    string Plan,
    string? Status,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool BillingEnabled,
    bool YearlyAvailable);

// Stripe subscriptions, paid on Stripe-hosted pages in the browser (not in-app purchase).
// /api/billing/* use the caller's API key; the webhook is signed by Stripe instead; the
// /billing/* pages are where the browser lands after Checkout or the portal, and they hand
// control back to the app via its deep link.
public static class BillingEndpoints
{
    public const string WebhookPath = "/api/stripe/webhook";

    // The app passes its own deep link (cardscout://… in a build, exp://… in Expo Go). Anything
    // else is replaced with the default, so the pages can't be used as an open redirect.
    private const string DefaultAppUrl = "cardscout://billing";
    private static readonly string[] AppSchemes = ["cardscout", "exp", "exps"];

    public static void MapBillingEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/billing/status", StatusAsync);
        routes.MapPost("/api/billing/checkout", CheckoutAsync);
        routes.MapPost("/api/billing/portal", PortalAsync);
        routes.MapPost(WebhookPath, WebhookAsync);

        routes.MapGet("/billing/success", SuccessPageAsync);
        routes.MapGet("/billing/cancel", (string? app) =>
            Page("Checkout canceled", "No charge was made. You can upgrade any time from Settings.", SafeAppUrl(app)));
        routes.MapGet("/billing/return", (string? app) =>
            Page("All set", "Your subscription changes are saved.", SafeAppUrl(app)));
    }

    private static async Task<IResult> StatusAsync(CurrentUser user, ISubscriptionService plans, StripeBillingService billing)
    {
        var plan = await plans.GetPlanAsync(user.UserId);
        return Results.Ok(new BillingStatusResponse(
            plan.Plan,
            plan.Status,
            plan.CurrentPeriodEnd is { } end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : null,
            plan.CancelAtPeriodEnd,
            billing.IsConfigured,
            billing.IsConfigured && billing.HasYearly));
    }

    private static async Task<IResult> CheckoutAsync(CheckoutRequest? body, CurrentUser user, StripeBillingService billing,
        StripeOptions options, HttpRequest request)
    {
        var appUrl = Uri.EscapeDataString(SafeAppUrl(body?.ReturnUrl));
        var baseUrl = $"{request.Scheme}://{request.Host}";
        // {CHECKOUT_SESSION_ID} is filled in by Stripe, so it must stay unescaped
        var successUrl = options.SuccessUrl ?? $"{baseUrl}/billing/success?session_id={{CHECKOUT_SESSION_ID}}&app={appUrl}";
        var cancelUrl = options.CancelUrl ?? $"{baseUrl}/billing/cancel?app={appUrl}";

        return await Run(async () =>
            new BillingUrlResponse(await billing.CreateCheckoutAsync(user.UserId, body?.Interval ?? "month", successUrl, cancelUrl)));
    }

    private static async Task<IResult> PortalAsync(PortalRequest? body, CurrentUser user, StripeBillingService billing,
        StripeOptions options, HttpRequest request)
    {
        var returnUrl = options.PortalReturnUrl
            ?? $"{request.Scheme}://{request.Host}/billing/return?app={Uri.EscapeDataString(SafeAppUrl(body?.ReturnUrl))}";
        return await Run(async () => new BillingUrlResponse(await billing.CreatePortalAsync(user.UserId, returnUrl)));
    }

    private static async Task<IResult> Run(Func<Task<BillingUrlResponse>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (BillingException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
        catch (StripeException ex)
        {
            return Results.Json(new { error = $"Stripe: {ex.StripeError?.Message ?? ex.Message}" }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // Stripe retries anything that isn't 2xx, so only a bad signature gets a 400 and real failures a 500
    private static async Task<IResult> WebhookAsync(HttpRequest request, StripeBillingService billing, ILogger<StripeBillingService> logger)
    {
        string signature = request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature))
            return Results.BadRequest(new { error = "Missing Stripe-Signature header." });

        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        try
        {
            var result = await billing.HandleWebhookAsync(json, signature);
            return Results.Ok(new { received = true, duplicate = result.Duplicate });
        }
        catch (BillingException ex)
        {
            logger.LogError("Stripe webhook rejected: {Message}", ex.Message);
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
        catch (StripeException ex) when (ex.StripeError == null)
        {
            logger.LogWarning("Stripe webhook with a bad signature or payload: {Message}", ex.Message);
            return Results.BadRequest(new { error = "Invalid Stripe signature." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe webhook failed; Stripe will retry");
            return Results.Json(new { error = "Webhook processing failed." }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> SuccessPageAsync([FromQuery(Name = "session_id")] string? sessionId, string? app, StripeBillingService billing,
        ILogger<StripeBillingService> logger)
    {
        // Sync now so the app shows Pro as soon as it reopens, even if the webhook is late or not wired up
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                await billing.SyncCheckoutSessionAsync(sessionId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't sync checkout session {SessionId}; the webhook will catch up", sessionId);
            }
        }
        return Page("You're subscribed", "Thanks for upgrading to CardScout Pro. Head back to the app to keep scouting.", SafeAppUrl(app));
    }

    private static string SafeAppUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && AppSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            ? url!
            : DefaultAppUrl;

    // A small dark page (matching the app) that bounces straight back into the app, with a button as fallback
    private static IResult Page(string title, string message, string appUrl)
    {
        var href = WebUtility.HtmlEncode(appUrl);
        var js = JsonSerializer.Serialize(appUrl);
        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}} · CardScout</title>
            <style>
              body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                     background: #06080D; color: #F3F5FA; font: 16px/1.5 -apple-system, system-ui, sans-serif; }
              main { max-width: 360px; padding: 32px 24px; text-align: center; }
              h1 { font-size: 26px; margin: 0 0 8px; }
              p { color: #9AA3B5; margin: 0 0 28px; }
              a { display: block; padding: 14px; border-radius: 12px; background: #5BF2B0; color: #04140D;
                  font-weight: 700; text-decoration: none; }
            </style>
            </head>
            <body>
            <main>
              <h1>{{WebUtility.HtmlEncode(title)}}</h1>
              <p>{{WebUtility.HtmlEncode(message)}}</p>
              <a href="{{href}}">Return to CardScout</a>
            </main>
            <script>setTimeout(function () { location.href = {{js}}; }, 600);</script>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html");
    }
}
