# CardScout

.NET 10 solution (`CardScout.slnx`) that looks for underpriced baseball cards on eBay to buy and resell. It compares eBay listings to SportsCardsPro market prices and flags the ones that would make a profit after fees and shipping.

## Projects

| Project | Type | Role |
|---|---|---|
| `CardScout.Core` | Class library | eBay clients (`Ebay/`), SportsCardsPro client (`Comps/`), title parsing/scoring (`Matching/`), Dapper repositories (`Data/`), models |
| `CardScout` | Worker Service | Hosted background services that poll and analyze |
| `CardScout.Web` | Razor Pages | Deals grid (`/Deals`, also `/`) and `/RecentListings`; runs at `https://localhost:7098` / `http://localhost:5088` |
| `CardScout.Api` | Minimal API | JSON API for the mobile app: card identification (Claude vision), inventory CRUD, card images. Dev URL `http://0.0.0.0:5090` (reachable on the LAN) |
| `mobile/` | Expo SDK 58 app | iPhone app (Expo Go, no custom native code): Scan / Collection / Deals tabs, dark-only theme (tokens in `mobile/src/lib/theme.ts`). See `mobile/README.md` |

### API (`CardScout.Api/`)

- Endpoints are in `Endpoints/CardEndpoints.cs`: `POST /api/cards/identify` (multipart `front`/`back`), `POST /api/cards/price` (re-price from card fields, optional `compProductId` to pin a comp), `POST /api/cards`, `GET /api/cards?status=`, `GET/PATCH /api/cards/{id}`, `GET /api/cards/{id}/images/{front|back}`. PATCH only changes the properties present in the body.
- **Multi-user.** `ApiKeyMiddleware` hashes `X-Api-Key`, looks it up in `CARD_USERS` and sets the scoped `CurrentUser`. Every inventory repository method takes `userId`, and images are stored under `users/{userId}/`. Keep it that way: **never add an unscoped inventory query**. Add users with `dotnet run --project CardScout.Api -- create-user --name X --email Y`.
- Identification is in `Core/Identify/CardIdentifier.cs`. It uses the Anthropic C# SDK (beta messages: structured JSON output, server-side refusal fallback), and the model and effort come from `Anthropic:*` in appsettings. Price quotes are in `Core/Pricing/CardPricingService.cs`, which reuses the SportsCardsPro match pipeline and returns the full grade ladder, runner-up candidates and net-after-fees (`SellerFees`, shared with `MatcherService`). It refreshes `COMP_CACHE` rows older than 3 days. `CompsClient` throws `CompsApiException` on SportsCardsPro error responses (e.g. an expired token) instead of returning no results. `CompPriceSelector` holds the grade→price mapping shared with `MatcherService`.
- Images go through `IImageStorage` (currently `LocalDiskImageStorage`, `CardScout.Api/card-images/`, gitignored).

### Workers (`CardScout/`)

- `Worker`: every 10 minutes, searches eBay for each active player in `WATCHED_PLAYERS` and saves auctions ending within about 1h plus newly listed Buy It Now items to `EBAY_LISTINGS`.
- `BroadSweepWorker`: once an hour, saves the 50 newest "baseball card" listings to `EBAY_LISTINGS`.
- `RecentListingsWorker`: every 10 minutes, saves the newest listings for the players in `TrackedPlayers` (hardcoded) to `RECENT_LISTINGS`.
- `MatcherService`: takes `PENDING` listings, parses the title (`TitleCleaner`), finds and scores SportsCardsPro candidates (`MatchScorer`, minimum confidence 0.55, results cached in `COMP_CACHE`), picks the price for the card's grade, computes net profit and writes a row to `DEALS`.
- `AvailabilityCheckWorker`: every 15 minutes, re-checks active deals on eBay and sets sold or removed listings to `SOLD`.

### Domain notes

- **Profit math.** Resale income = comp × (1 − 13.25%) − $0.40 per-order fee − $0.75 packaging. Cost = price + shipping; until the real shipping is known it is estimated by price tier. A deal is profitable at **≥ $2 and ≥ 5%** net profit. The real shipping is only fetched from eBay for listings that already look profitable, to save eBay API calls.
- **SportsCardsPro price fields reuse video-game names.** `loose` = raw, `cib` = 7, `new` = 8, `graded` = 9, `box-only` = 9.5, `manual-only` = PSA 10, `bgs-10` = BGS 10. Prices come back in pennies.
- **SportsCardsPro is throttled** to about 1 call per second in `CompsClient`. Keep that limit.
- **Status values.** `EBAY_LISTINGS.MATCH_STATUS`: `PENDING` → `MATCHED` / `NO_MATCH` / `ERROR`, and later `SOLD`. `DEALS.USER_STATUS`: `KEPT` / `REMOVED` (set from the grid; `REMOVED` rows are hidden).

## Secrets

All secrets live in .NET user-secrets, not in the repo. Never commit them or hardcode them.

- `ConnectionStrings:Oracle`
- `Ebay:AppId` and `Ebay:CertId`
- `SportsCardsPro:Token` (worker and API)
- `Anthropic:ApiKey` (API only)

The worker, web and API projects each have their own `UserSecretsId`, so set each value in every project that uses it. User API keys are not config: only their SHA-256 hashes are stored, in `CARD_USERS`. The mobile app reads `EXPO_PUBLIC_API_URL` / `EXPO_PUBLIC_API_KEY` from `mobile/.env` (gitignored). Check them with `dotnet user-secrets list --project CardScout` or `--project CardScout.Web`.

## Database

CardScout uses the **shared Zenith Oracle database**: Oracle 21c EE at `db.zenith.polymarket.com:1521/POLYMARKETDATA`, user/schema `ZENITH_DBA`. The connection string is in user-secrets (see above). Most tables in this schema belong to other projects (Polymarket, Kalshi, Binance, payouts…).

`OracleConfiguration.BindByName = true` is set at worker startup, so use named `:Param` binds in Dapper SQL.

### CardScout tables

- `EBAY_LISTINGS`: raw eBay listings, keyed by `ITEM_ID`, with `MATCH_STATUS`
- `COMP_CACHE`: SportsCardsPro matches and prices, keyed by search query
- `DEALS`: one row per matched listing, with comp price, discount, net profit, confidence, `IS_PROFITABLE` (`Y`/`N`), `USER_STATUS`
- `V_DEALS_DETAIL`: view joining deals and listings, used by the web grid
- `RECENT_LISTINGS`: newest listings for the players being tracked
- `CARD_USERS`: app users (API key hash now; `AUTH_PROVIDER`/`AUTH_SUBJECT` reserved for real sign-in later)
- `CARD_INVENTORY`: cards users own, with `USER_ID` (FK to `CARD_USERS`, NOT NULL), `STATUS` (`UNLISTED`/`LISTED`/`SOLD`)
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
