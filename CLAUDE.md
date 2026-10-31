# CardScout

.NET 10 solution (`CardScout.slnx`) for pricing sports cards and trading card game (TCG) cards (Pokemon, Magic, Yu-Gi-Oh!, One Piece, Lorcana...), finding underpriced buys and flipping them for profit. It compares eBay listings (and cards scanned at card shows) to SportsCardsPro / PriceCharting market prices and flags the ones that would make a profit after fees and shipping.

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
- **Multi-user.** `ApiKeyMiddleware` hashes `X-Api-Key`, looks it up in `CARD_USERS` and sets the scoped `CurrentUser`. Every inventory, watchlist, push-token and price-history repository method takes `userId`, and images are stored under `users/{userId}/`. Keep it that way: **never add an unscoped user-data query**. Workers loop over `CardUsersRepository.GetActiveIdsAsync()` and make user-scoped calls. Add users with `dotnet run --project CardScout.Api -- create-user --name X --email Y`.
- Identification is in `Core/Identify/CardIdentifier.cs`. It uses the Anthropic C# SDK (beta messages: structured JSON output, server-side refusal fallback), and the model and effort come from `Anthropic:*` in appsettings. Price quotes are in `Core/Pricing/CardPricingService.cs`, which reuses the SportsCardsPro match pipeline and returns the full grade ladder, runner-up candidates and net-after-fees (`SellerFees`, shared with `MatcherService`). It refreshes `COMP_CACHE` rows older than 3 days. `CompsClient` throws `CompsApiException` on SportsCardsPro error responses (e.g. an expired token) instead of returning no results. `CompPriceSelector` holds the grade→price mapping shared with `MatcherService`.
- **Flip math** is `Core/Flip/FlipAnalyzer.cs` (verdict `STEAL`/`BUY`/`FAIR`/`OVERPRICED`/`UNKNOWN`, max buy price / max bid, opening offer). `mobile/src/lib/flip.ts` mirrors it so show mode updates instantly; change both together. Grading ROI is computed only in the app (`analyzeGrading` in `flip.ts`), using the AI `gradeOutlook` from identification or a preset.
- `Core/Flip/ListingEvaluator.cs` matches a listing title to a comp (COMP_CACHE → search → `MatchScorer`); it's shared by `MatcherService` and `WatchlistWorker`. `MatchAsync(title, category, game)` takes the TCG path when category is `tcg`.
- **Trading card games.** Identification returns `category` (`sports`/`tcg`), `game` (`pokemon`, `mtg`, `yugioh`, `one-piece`, `lorcana`, `other`), `setCode`, `rarity` and `language`. TCG cards reuse the sports fields: `player` = card name, `set` = set/expansion, `cardNumber` = collector number as printed (`4/102`, `LOB-EN001`, `OP01-024`), `parallel` = variant/finish (`1st Edition`, `Reverse Holo`, `Foil`), `manufacturer` = publisher. All TCG search/scoring lives in `Core/Matching/TcgMatcher.cs` (name + set + number + bracketed variant + language; no year/player/color logic); `TitleCleaner`/`MatchScorer` stay sports-only. `CardPricingService`, `ListingEvaluator`, `MarketSnapshotService`, `ListingKitBuilder` and the watchlist worker branch on the category.
- **TCG comps come from PriceCharting**, not SportsCardsPro: SportsCardsPro's search only returns sports cards. `CompsClient.SearchAsync(query, tradingCardGames: true)` searches `pricecharting.com` with the same token (same company, same API, same price fields; product ids are shared, so `/api/product` works on either host). PriceCharting consoles look like `Pokemon Base Set`, `Pokemon Japanese Expansion Pack` (Japanese base set), `Magic Dominaria United`, `YuGiOh Legend of Blue Eyes White Dragon`, `One Piece Romance Dawn`, `Lorcana First Chapter`; products like `Charizard [1st Edition] #4`, and Yu-Gi-Oh!/One Piece codes without `#` (`Blue-Eyes White Dragon LOB-001`). Non-English printings are separate consoles (`Pokemon Japanese ...`, `Chinese`, `Korean`). TCG cache keys in `COMP_CACHE` are prefixed `tcg|`.
- **Scan flow.** The Scan tab sends the front to `/api/cards/identify` as soon as it's captured (while the user lines up the back). TCG backs are identical within a game, so a TCG result goes straight to review with no back photo; sports cards keep the front+back flow (back skippable; with a back it's re-identified front+back, so a sports scan with a back costs two Claude calls). Show mode and lots are front-only and work for both.
- eBay searches use category `212` (sports cards) or `2536` (Collectible Card Games) via `EbaySearchClient.CategoryFor(category)`. `CARD_WATCHLISTS.CATEGORY`/`GAME` pick the category and title matching for a watchlist.
- The API needs `Ebay:AppId`/`Ebay:CertId` too (market panel), in addition to the worker.
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
- **SportsCardsPro is throttled** to about 1 call per second in `CompsClient` (shared with PriceCharting calls, same company and token). Keep that limit. A TCG quote can take up to 3 searches when the first one has no confident match.
- **TCG grade fields** are the same as sports (checked on Base Set Charizard and Evolving Skies Umbreon VMAX): `loose` = ungraded, `cib` = 7, `new` = 8, `graded` = 9, `box-only` = 9.5, `manual-only` = PSA 10, `bgs-10`, `condition-17` = CGC 10, `condition-18` = SGC 10. PriceCharting also returns `genre` (e.g. `Pokemon Card`) and `tcg-id` (TCGplayer id), which aren't stored.
- **eBay quota is per application**, not per user (Browse API ~5,000 calls/day by default; raise it with eBay's free Application Growth Check). `WatchlistWorker` shares one search between watchlists with the same query per sweep. Sold-item data (Marketplace Insights API) is invite-only and not available to this app; the app links out to eBay's sold search instead.
- **Scan costs.** Each identify logs its Claude model and token counts (`Scan by user ...` in the API log).
- **Status values.** `EBAY_LISTINGS.MATCH_STATUS`: `PENDING` → `MATCHED` / `NO_MATCH` / `ERROR`, and later `SOLD`. `DEALS.USER_STATUS`: `KEPT` / `REMOVED` (set from the grid; `REMOVED` rows are hidden). `WATCHLIST_HITS.USER_STATUS`: `NEW` / `SAVED` / `DISMISSED` / `BOUGHT`.
- **Push notifications** go through Expo's push service (`ExpoPushClient`); tokens are in `CARD_PUSH_TOKENS` and tokens Expo reports as `DeviceNotRegistered` are deleted. The app needs an EAS project id (`npx eas-cli init` in `mobile/`) to get a token; Android needs a development build.

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
- `CARD_INVENTORY`: cards users own, with `USER_ID` (FK to `CARD_USERS`, NOT NULL), `STATUS` (`UNLISTED`/`LISTED`/`SOLD`), P&L fields (`PURCHASE_SOURCE`, `PURCHASED_AT`, `GRADING_COST`, `SALE_SHIPPING_COST`) and `MARKET_PRICE`/`MARKET_PRICE_AT` (worker-owned; `SUGGESTED_PRICE` is the user's)
- `CARD_WATCHLISTS` / `WATCHLIST_HITS`: per-user saved eBay searches and every listing they've priced (unique per watchlist + item). `CATEGORY` (`sports`/`tcg`, NULL = sports) and `GAME` on the watchlist
- `CARD_INVENTORY` TCG columns (`db/007_tcg.sql`): `CATEGORY` (`sports`/`tcg`, NULL = sports), `GAME`, `SET_CODE`, `RARITY`, `CARD_LANGUAGE`
- `CARD_PUSH_TOKENS`: Expo push tokens per user/device
- `CARD_PRICE_HISTORY`: market price snapshots for held cards
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
