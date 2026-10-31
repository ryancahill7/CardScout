# CardScout

.NET 10 solution (`CardScout.slnx`) for pricing sports cards, finding underpriced buys and flipping them for profit. It compares eBay listings (and cards scanned at card shows) to SportsCardsPro market prices and flags the ones that would make a profit after fees and shipping.

## Projects

| Project | Type | Role |
|---|---|---|
| `CardScout.Core` | Class library | eBay clients (`Ebay/`), SportsCardsPro client (`Comps/`), title parsing/scoring (`Matching/`), flip math and listing evaluation (`Flip/`), listing kits (`Listing/`), Expo push (`Notifications/`), Dapper repositories (`Data/`), models |
| `CardScout` | Worker Service | Hosted background services that poll and analyze |
| `CardScout.Web` | Razor Pages | Deals grid (`/Deals`, also `/`) and `/RecentListings`; runs at `https://localhost:7098` / `http://localhost:5088` |
| `CardScout.Api` | Minimal API | JSON API for the mobile app: card identification (Claude vision), inventory CRUD, card images. Dev URL `http://0.0.0.0:5090` (reachable on the LAN) |
| `mobile/` | Expo SDK 58 app | iPhone app (Expo Go, no custom native code): Scan / Show / Deals / Collection tabs, dark-only theme (tokens in `mobile/src/lib/theme.ts`). See `mobile/README.md` |

### API (`CardScout.Api/`)

- Endpoints are in `Endpoints/`:
  - `CardEndpoints.cs`: `POST /api/cards/identify` (multipart `front`, optional `back`; front-only is show mode), `POST /api/cards/price` (re-price from card fields, optional `compProductId` to pin a comp), `POST /api/cards/market` (active eBay listings for the card: asking prices, not sales), `POST /api/cards`, `GET /api/cards?status=`, `GET/PATCH /api/cards/{id}`, `GET /api/cards/{id}/images/{front|back}`, `GET /api/cards/{id}/listing-kit`, `GET /api/cards/{id}/history`. PATCH only changes the properties present in the body.
  - `LotEndpoints.cs`: `POST /api/lots/analyze` (multipart `photo` plus optional `askingPrice`, `shipping`, `minProfit`, `minProfitPct`): Claude finds every card in one photo, each is priced, and `FlipAnalyzer.AnalyzeLot` judges the total.
  - `WatchlistEndpoints.cs`: `GET/POST /api/watchlists`, `PUT/DELETE /api/watchlists/{id}`, `GET /api/watchlists/hits?status=&all=&watchlistId=`, `GET/PATCH /api/watchlists/hits/{id}`.
  - `PortfolioEndpoints.cs`: `GET /api/portfolio` (P&L, monthly, movers), `GET /api/portfolio/export` (CSV), `GET /api/deals` (shared `DEALS` grid, profitable only), `POST/DELETE /api/push-tokens`, `POST /api/push-tokens/test`.
  - `BillingEndpoints.cs`: `GET /api/billing/status` (`{ plan: "free"|"pro", status, currentPeriodEnd, cancelAtPeriodEnd, billingEnabled, yearlyAvailable }`), `POST /api/billing/checkout` (`{ interval: "month"|"year", returnUrl? }` → `{ url }` of a Stripe Checkout page), `POST /api/billing/portal` (`{ returnUrl? }` → `{ url }` of the Stripe Billing Portal), `POST /api/stripe/webhook` (no API key; verified by `Stripe-Signature`), and the browser landing pages `GET /billing/success|cancel|return?app=` that bounce back into the app (`returnUrl`/`app` must be a `cardscout://`, `exp://` or `exps://` link).
- **Subscriptions (Stripe, paid in the browser, not in-app purchase).** `Core/Billing/StripeBillingService.cs` creates the Stripe customer (one per user, `CARD_SUBSCRIPTIONS.STRIPE_CUSTOMER_ID`, metadata `userId`), Checkout and portal sessions, and handles webhooks: `checkout.session.completed`, `customer.subscription.created/updated/deleted`, `invoice.payment_failed`. Each event re-reads the subscription from Stripe (events can arrive out of order) and is recorded in `CARD_STRIPE_EVENTS` so redeliveries are no-ops. The success page also syncs the session, so the plan updates even if the webhook is late. **To check whether a user is Pro, inject `ISubscriptionService` and call `GetPlanAsync(userId)` / `IsProAsync(userId)`** (Stripe status `active` or `trialing` = pro; cached 60 s, invalidated by webhooks). Without Stripe keys everything still runs: every user is free and checkout returns 503. The app side is `mobile/src/lib/billing.ts` (`getBillingStatus`, `openCheckout`, `openBillingPortal`, `useBillingStatus`) and the Subscription section in Settings.
  - `UsageEndpoints.cs`: `GET /api/usage` → `{ plan, usedUsd, limitUsd, percentUsed, resetsAt, scansThisMonth }` (calendar month, UTC), and `GET /api/plans` (the plan catalog the paywall shows).
- **Multi-user.** `ApiKeyMiddleware` hashes `X-Api-Key`, looks it up in `CARD_USERS` and sets the scoped `CurrentUser`. Every inventory, watchlist, push-token and price-history repository method takes `userId`, and images are stored under `users/{userId}/`. Keep it that way: **never add an unscoped user-data query**. Workers loop over `CardUsersRepository.GetActiveIdsAsync()` and make user-scoped calls. Add users with `dotnet run --project CardScout.Api -- create-user --name X --email Y`.
- **Multi-user.** `ApiKeyMiddleware` authenticates every `/api` request and sets the scoped `CurrentUser` (the only thing endpoints use). It accepts `Authorization: Bearer <access JWT>` (the app after sign-in) or `X-Api-Key` (hashed, looked up in `CARD_USERS`; for scripts, dev and existing users). Endpoints marked `.AllowAnonymous()` skip it (the auth endpoints; use the same for webhooks). Every inventory, watchlist, push-token and price-history repository method takes `userId`, and images are stored under `users/{userId}/`. Keep it that way: **never add an unscoped user-data query**. Workers loop over `CardUsersRepository.GetActiveIdsAsync()` and make user-scoped calls. Add users with `dotnet run --project CardScout.Api -- create-user --name X --email Y`.
- **Sign-in** (`CardScout.Api/Auth/`, `Endpoints/AuthEndpoints.cs`, all under `/api/auth`):
  - Email + password: `POST signup`, `POST signin`. Passwords are hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2). Emails are trimmed and lowercased. `PasswordPolicy` requires 10-128 characters and checks a common-password list. Errors are generic, and unknown emails get the same lockout as real ones. 5 failed passwords lock the account for 15 minutes. There are also per-IP rate limits: `auth` (20/min) and `auth-strict` (signup and reset, a 10-token bucket that refills 1 per minute).
  - Sessions: a 15-minute HS256 access JWT plus a 60-day refresh token. The refresh token is stored as a SHA-256 hash in `CARD_REFRESH_TOKENS` and rotates on every `POST refresh`. Presenting a token that was already used revokes its whole family (that sign-in session). Other endpoints: `POST logout` (revokes the family), `GET me`, `GET providers` (which buttons the app shows).
  - Federated: `POST apple` (identity token + raw nonce, verified against Apple's JWKS), `POST google` (ID token, for a future native build), and browser flows brokered by the API for `google`, `ebay` and `oidc`: `GET {p}/start?redirectUri&codeChallenge[&email]` → provider → `GET {p}/callback` → app deep link with a one-time code → `POST {p}/complete {code, codeVerifier}`. Tokens never go in a URL. Identities live in `CARD_USER_IDENTITIES`. A sign-in links to an existing user by email only when the provider says the email is verified, and linking drops a password that was set on an unverified email (pre-hijack defence). eBay never links by email.
  - Password reset: `POST password/forgot` (always 202) → single-use 30-minute token (hashed, `CARD_PASSWORD_RESETS`) → `POST password/reset`, which also verifies the email and revokes all sessions. Mail goes through `IEmailSender`. The current `LoggingEmailSender` writes the link to the API log in Development only. **No real email provider yet.**
  - `DELETE /api/auth/account` deletes the user and every row in `AccountDeletionRepository.UserTables`, plus their `users/{id}/` images. **Add any new table with a `USER_ID` to that list.**
  - The mobile auth gate is `Stack.Protected` in `mobile/src/app/_layout.tsx`. Its screens are in `src/app/(auth)/`, and the session (expo-secure-store, refresh on 401, single-flight) is in `src/lib/auth.ts`. `EXPO_PUBLIC_API_KEY` still works as a "Use dev API key" session.
- Identification is in `Core/Identify/CardIdentifier.cs`. It uses the Anthropic C# SDK (beta messages: structured JSON output, server-side refusal fallback), and the model and effort come from `Anthropic:*` in appsettings. Price quotes are in `Core/Pricing/CardPricingService.cs`, which reuses the SportsCardsPro match pipeline and returns the full grade ladder, runner-up candidates and net-after-fees (`SellerFees`, shared with `MatcherService`). It refreshes `COMP_CACHE` rows older than 3 days. `CompsClient` throws `CompsApiException` on SportsCardsPro error responses (e.g. an expired token) instead of returning no results. `CompPriceSelector` holds the grade→price mapping shared with `MatcherService`.
- **Flip math** is `Core/Flip/FlipAnalyzer.cs` (verdict `STEAL`/`BUY`/`FAIR`/`OVERPRICED`/`UNKNOWN`, max buy price / max bid, opening offer). `mobile/src/lib/flip.ts` mirrors it so show mode updates instantly; change both together. Grading ROI is computed only in the app (`analyzeGrading` in `flip.ts`), using the AI `gradeOutlook` from identification or a preset.
- `Core/Flip/ListingEvaluator.cs` matches a listing title to a comp (COMP_CACHE → search → `MatchScorer`); it's shared by `MatcherService` and `WatchlistWorker`.
- The API needs `Ebay:AppId`/`Ebay:CertId` too (market panel), in addition to the worker.
- **Plans and AI metering.** Plans are defined once, in `Core/Billing/Plans.cs` (`PlanCatalog`). Limits and prices can be overridden under `Billing:Plans:{free|pro}:{AiMonthlyLimitUsd|MaxWatchlists|MonthlyPriceUsd|YearlyPriceUsd}`. `ISubscriptionService.GetPlanAsync(userId)` returns `free` or `pro` (`Core/Billing/ISubscriptionService.cs`; Stripe implements it). Every Claude call goes through `UsageMeter` (`Core/Usage/`): `CheckAsync` runs before the call, and `RecordAsync` runs after it, including after failed calls that were still billed (`CardIdentificationException.Usage`). Each call writes one `CARD_AI_USAGE` row with its tokens and its `COST_USD` from `AiCostCalculator`. Default $/MTok per model is in code; override it under `AnthropicPricing:{model}:{InputPerMTok|OutputPerMTok|CacheWritePerMTok|CacheReadPerMTok}`. A model with no price is charged at the highest known rate, and a warning is logged. **A new Claude feature needs an `AiFeatures` constant, a `Billing:CallEstimatesUsd` entry, and calls to `CheckAsync`/`RecordAsync`.**
- **The 402 contract.** The API returns HTTP 402 `{ code: "USAGE_LIMIT", error, plan, usedUsd, limitUsd, resetsAt }` without calling Claude when the month-to-date cost plus the feature's estimate (`Billing:CallEstimatesUsd`) would go over the plan's `AiMonthlyLimitUsd`. That limit is $2.00 for Free (which also uses the cheaper `Anthropic:FreeModel`, Sonnet 5.5, while Pro uses `Anthropic:Model`), and $20.00 for Pro as a fair-use guard. Limits that aren't about AI, such as active watchlists (Free 3, Pro 25; enforced on create and on re-activation), return 402 `{ code: "PLAN_LIMIT", error, plan, feature, limit }`. Features that don't use AI (pricing by fields, market, deals, collection) are never capped. `mobile/src/lib/api.ts` handles every 402 in one place: the root layout opens the `/upgrade` paywall modal. When a free user passes `Billing:WarnAtPercent` (80%), they get one Expo push per month (tracked in `CARD_USAGE_ALERTS`) and an in-app banner (`components/usage.tsx`).
- Images go through `IImageStorage` (currently `LocalDiskImageStorage`, `CardScout.Api/card-images/`, gitignored).

### Workers (`CardScout/`)

- `Worker`: every 10 minutes, searches eBay for each active player in `WATCHED_PLAYERS` and saves auctions ending within about 1h plus newly listed Buy It Now items to `EBAY_LISTINGS`.
- `BroadSweepWorker`: once an hour, saves the 50 newest "baseball card" listings to `EBAY_LISTINGS`.
- `RecentListingsWorker`: every 10 minutes, saves the newest listings for the players in `TrackedPlayers` (hardcoded) to `RECENT_LISTINGS`.
- `MatcherService`: takes `PENDING` listings, parses the title (`TitleCleaner`), finds and scores SportsCardsPro candidates (`MatchScorer`, minimum confidence 0.55, results cached in `COMP_CACHE`), picks the price for the card's grade, computes net profit and writes a row to `DEALS`.
- `AvailabilityCheckWorker`: every 15 minutes, re-checks active deals on eBay and sets sold or removed listings to `SOLD`.
- `WatchlistWorker`: every 5 minutes, runs each user's active `CARD_WATCHLISTS` searches on eBay (BIN newly listed + auctions ending within 24h), prices up to 15 unseen listings per list per run, saves every one to `WATCHLIST_HITS`, and pushes an alert (Expo) for listings that clear the list's `$`/`%` targets.
- `PortfolioPriceWorker`: hourly, re-prices held inventory cards whose `MARKET_PRICE` is over ~a day old (pinned to `COMP_PRODUCT_ID`), writes `CARD_PRICE_HISTORY`, and pushes when a card moves ≥ 15% and ≥ $3 over a week.

### Domain notes

- **Profit math.** Resale income = comp × (1 − 13.25%) − $0.40 per-order fee − $0.75 packaging. Cost = price + shipping; until the real shipping is known it is estimated by price tier. A deal is profitable at **≥ $2 and ≥ 5%** net profit. The real shipping is only fetched from eBay for listings that already look profitable, to save eBay API calls.
- **SportsCardsPro price fields reuse video-game names.** `loose` = raw, `cib` = 7, `new` = 8, `graded` = 9, `box-only` = 9.5, `manual-only` = PSA 10, `bgs-10` = BGS 10. Undocumented `condition-N` fields carry the rest: `condition-9` = Grade 1, `-10` = 2, `-13` = 3, `-14` = 4, `-15` = 5, `-16` = 6, `-17` = CGC 10, `-18` = SGC 10 (mapped against a card's real per-grade sales; `-19`/`-20` are unmapped). `sales-volume` is the number of recorded sales. Prices come back in pennies. `CompPriceSelector` maps grades to these tiers; `COMP_CACHE` rows cached before 2026-10-31 lack grades 1-6 and get refreshed on use (`CompCacheRecord.HasFullLadder`).
- **SportsCardsPro is throttled** to about 1 call per second in `CompsClient`. Keep that limit.
- **eBay quota is per application**, not per user (Browse API ~5,000 calls/day by default; raise it with eBay's free Application Growth Check). `WatchlistWorker` shares one search between watchlists with the same query per sweep. Sold-item data (Marketplace Insights API) is invite-only and not available to this app; the app links out to eBay's sold search instead.
- **Scan costs.** Each Claude call logs its model, tokens and cost (`AI identify by user ...` in the API log) and is stored in `CARD_AI_USAGE`. At `claude-opus-5-5` ($4/$20 per MTok) with medium effort, a front+back scan is estimated at about $0.06-0.08 and a lot photo at $0.10-0.15. Check these against real `CARD_AI_USAGE` rows and update `Billing:CallEstimatesUsd`.
- **Status values.** `EBAY_LISTINGS.MATCH_STATUS`: `PENDING` → `MATCHED` / `NO_MATCH` / `ERROR`, and later `SOLD`. `DEALS.USER_STATUS`: `KEPT` / `REMOVED` (set from the grid; `REMOVED` rows are hidden). `WATCHLIST_HITS.USER_STATUS`: `NEW` / `SAVED` / `DISMISSED` / `BOUGHT`.
- **Push notifications** go through Expo's push service (`ExpoPushClient`); tokens are in `CARD_PUSH_TOKENS` and tokens Expo reports as `DeviceNotRegistered` are deleted. The app needs an EAS project id (`npx eas-cli init` in `mobile/`) to get a token; Android needs a development build.

## Secrets

All secrets live in .NET user-secrets, not in the repo. Never commit them or hardcode them.

- `ConnectionStrings:Oracle`
- `Ebay:AppId` and `Ebay:CertId`
- `SportsCardsPro:Token` (worker and API)
- `Anthropic:ApiKey` (API only)
- `Stripe:SecretKey` and `Stripe:WebhookSecret` (API only). The non-secret `Stripe:PriceIdMonthly`, optional `Stripe:PriceIdYearly`, and optional URL overrides `Stripe:SuccessUrl` / `Stripe:CancelUrl` / `Stripe:PortalReturnUrl` can go in user-secrets too (defaults: the API's own `/billing/*` pages)
- `Auth:JwtSigningKey` (API only, required: base64 of at least 32 random bytes; the API won't start without it)
- Optional sign-in providers (API only; a provider with missing settings is hidden in the app):
  - Google: `Auth:Google:WebClientId` + `Auth:Google:WebClientSecret` (browser flow), and `Auth:Google:ClientIds` (array; audiences accepted by `POST /api/auth/google`)
  - Apple: `Auth:Apple:Audiences` (array of bundle ids / Services IDs; `appsettings.Development.json` already has `host.exp.Exponent` for Expo Go)
  - eBay: `Auth:Ebay:RuName` (plus the existing `Ebay:AppId`/`Ebay:CertId`), `Auth:Ebay:Environment` (`production`/`sandbox`)
  - OIDC SSO: `Auth:Oidc:Authority`, `Auth:Oidc:ClientId`, optional `Auth:Oidc:ClientSecret`, `Auth:Oidc:DisplayName`, `Auth:Oidc:Scopes`, `Auth:Oidc:Domains` (array; if set, the app asks for a work email)
  - `Auth:PublicBaseUrl`: the public https URL of the API, used to build `{PublicBaseUrl}/api/auth/{google|ebay|oidc}/callback`. Providers won't accept a LAN `http://` URL, so the browser flows need a tunnel or a deployed API.

The worker, web and API projects each have their own `UserSecretsId`, so set each value in every project that uses it. User API keys are not config: only their SHA-256 hashes are stored, in `CARD_USERS`. The mobile app reads `EXPO_PUBLIC_API_URL` / `EXPO_PUBLIC_API_KEY` from `mobile/.env` (gitignored). Check them with `dotnet user-secrets list --project CardScout` or `--project CardScout.Web`.

### Stripe test mode setup

1. In the Stripe dashboard (test mode), create a product "CardScout Pro" with a recurring monthly price (and optionally a yearly one). Copy the `price_...` ids.
2. In Settings → Billing → Customer portal, activate the portal (allow cancel, and plan switching if you add a yearly price).
3. Set the secrets on the API project:
   ```
   dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project CardScout.Api
   dotnet user-secrets set "Stripe:PriceIdMonthly" "price_..." --project CardScout.Api
   dotnet user-secrets set "Stripe:PriceIdYearly" "price_..." --project CardScout.Api   # optional
   ```
4. Forward webhooks to the local API with the Stripe CLI (free) and set the signing secret it prints:
   ```
   stripe listen --forward-to http://localhost:5090/api/stripe/webhook \
     --events checkout.session.completed,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,invoice.payment_failed
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..." --project CardScout.Api
   ```
   In production, add a webhook endpoint in the dashboard for `https://<api-host>/api/stripe/webhook` with those events instead.
5. Restart the API, open Settings in the app, tap Upgrade, and pay with card `4242 4242 4242 4242` (any future date, any CVC). `stripe trigger customer.subscription.updated` etc. exercise the webhook. Stripe's Checkout page must be able to reach the success URL, which is the API's address as the phone sees it (e.g. `http://192.168.x.x:5090/billing/success`).

## Database

CardScout uses the **shared Zenith Oracle database**: Oracle 21c EE at `db.zenith.polymarket.com:1521/POLYMARKETDATA`, user/schema `ZENITH_DBA`. The connection string is in user-secrets (see above). Most tables in this schema belong to other projects (Polymarket, Kalshi, Binance, payouts…).

`OracleConfiguration.BindByName = true` is set at worker startup, so use named `:Param` binds in Dapper SQL.

### CardScout tables

- `EBAY_LISTINGS`: raw eBay listings, keyed by `ITEM_ID`, with `MATCH_STATUS`
- `COMP_CACHE`: SportsCardsPro matches and prices, keyed by search query
- `DEALS`: one row per matched listing, with comp price, discount, net profit, confidence, `IS_PROFITABLE` (`Y`/`N`), `USER_STATUS`
- `V_DEALS_DETAIL`: view joining deals and listings, used by the web grid
- `RECENT_LISTINGS`: newest listings for the players being tracked
- `CARD_USERS`: app users. Columns: optional API key hash, `PASSWORD_HASH`, `EMAIL_VERIFIED`, lockout columns. `AUTH_PROVIDER`/`AUTH_SUBJECT` are unused; identities are in `CARD_USER_IDENTITIES`.
- `CARD_USER_IDENTITIES` (external logins, unique provider + subject), `CARD_REFRESH_TOKENS`, `CARD_PASSWORD_RESETS`, `CARD_AUTH_FLOWS` (brokered browser sign-in state + one-time codes). All are from `db/008_auth.sql`. Their times are UTC (`SYS_EXTRACT_UTC(SYSTIMESTAMP)`).
- `CARD_INVENTORY`: cards users own, with `USER_ID` (FK to `CARD_USERS`, NOT NULL), `STATUS` (`UNLISTED`/`LISTED`/`SOLD`), P&L fields (`PURCHASE_SOURCE`, `PURCHASED_AT`, `GRADING_COST`, `SALE_SHIPPING_COST`) and `MARKET_PRICE`/`MARKET_PRICE_AT` (worker-owned; `SUGGESTED_PRICE` is the user's)
- `CARD_WATCHLISTS` / `WATCHLIST_HITS`: per-user saved eBay searches and every listing they've priced (unique per watchlist + item)
- `CARD_PUSH_TOKENS`: Expo push tokens per user/device
- `CARD_PRICE_HISTORY`: market price snapshots for held cards
- `CARD_SUBSCRIPTIONS`: one row per user with their Stripe customer/subscription ids, `PLAN` (`FREE`/`PRO`), Stripe `STATUS`, `CURRENT_PERIOD_END`, `CANCEL_AT_PERIOD_END`; written by the API and the Stripe webhook
- `CARD_STRIPE_EVENTS`: Stripe webhook event ids already processed (idempotency)
- `CARD_AI_USAGE`: one row per Claude call: `FEATURE` (`identify`/`identify_front`/`lot`), `MODEL`, token counts including cache, `COST_USD`, `PLAN` and `SUCCEEDED`. The app writes `CREATED_AT` in UTC.
- `CARD_USAGE_ALERTS`: usage-warning pushes already sent (one per user, month and kind)
- DDL for CardScout tables is in `db/` as numbered scripts. Add a new script for each schema change.
- `WATCHED_PLAYERS`: players the main `Worker` searches (`ACTIVE = 'Y'`). This table is shared with the Zenith sports projects.

### Querying from the command line

Use the Zenith query tool. It needs `py -m pip install oracledb`. Run it with `py`, not `python`, because `python` on this machine is the Windows Store stub.

```
py ../Zenith-Reboot/tools/db/query.py --describe EBAY_LISTINGS
py ../Zenith-Reboot/tools/db/query.py "select * from deals where is_profitable = 'Y' fetch first 20 rows only"
py ../Zenith-Reboot/tools/db/query.py "update ..." --commit   # DML rolls back unless --commit
```

DDL (`create`, `alter`, `drop`) auto-commits in Oracle, whether or not you pass `--commit`.

### Rules when working with the DB

- **Only touch CardScout tables.** Other projects own everything else in this schema. Don't change it.
- **Huge tables live here too.** Never `count(*)` or run unfiltered scans on them, e.g. `POLY_MARKET_PRICE_CHANGES` (~11B rows), `POLY_MARKET_BID_ASKS` (~3.4B), `POLY_MARKET_BEST_BID_ASK` (~950M), `BINANCE_CRYPTO_PRICE_HISTORY` (~258M). Use `FETCH FIRST n ROWS ONLY` everywhere.
- **Ask first.** Get the user's OK before dropping or truncating tables, or before running `update`/`delete` that touches existing data. Creating new tables, views and indexes is fine when the user asks for it.
- **New tables.** Use UPPER_SNAKE_CASE. CardScout uses Dapper and raw SQL, not EF, so add a repository in `CardScout.Core/Data/` and register it as a singleton in each `Program.cs` that needs it.
- **Inspect before writing SQL.** Use `--describe` to check column names before writing queries or code against a table.

## Conventions

- Repositories take the connection string in their constructor, open a new `OracleConnection` per call, and are registered as singletons.
- Workers are `BackgroundService` loops that catch and log errors per item, so one failure doesn't stop the loop.
- There is no test project yet.
