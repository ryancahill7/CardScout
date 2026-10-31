# CardScout Mobile

Expo (SDK 58) app for photographing cards, identifying them with Claude, and tracking them in your CardScout inventory. It runs in **Expo Go** on iPhone. There's no custom native code, so EAS cloud builds work later without a Mac.

- **Scan:** front photo first; it's read while you line up the back. Pokémon, Magic, Yu-Gi-Oh!, One Piece and Lorcana cards (whose backs are all the same) go straight to review. Sports cards ask for the back (or **Skip**), then **Identify & price**. The card form switches between sports fields and TCG fields (game, set, set code, number, rarity, variant/finish, language). You get the market value for the card's grade, the full grade ladder (tap a tier to use that price), net after eBay fees, and profit against your cost. If you fix a field, tap **Re-price**. If the match is wrong, use **Wrong card?** to pick another comp. **Buying it?** takes an asking price and gives a buy/fair/pass verdict, **Grade it?** shows grading ROI per service (from Claude's read of centering/corners, or a preset), and **On eBay now** lists the cheapest active listings. Then **Add to collection**.
- **Show:** card-show mode. Snap just the front of a card; it's identified and priced in the background while you keep scanning. Set what share of sold comps you pay in cash (e.g. 80%, with − / + buttons) and whether you resell in person (no fees) or on eBay. Type the dealer's asking price for a verdict (Steal / Buy / Fair / Pass), your max price, profit and an opening offer. **Sold comps** shows what the card sells for at its grade and the grades around it, with links to eBay's sold listings, the sales history and, for slabs, the grader's cert lookup. **Bought it** adds the card to your collection with its cost and source. **Lot** prices every card in one photo (a lot, binder page or display case) against the lot's asking price.
- **Deals:** **My alerts** are listings your watchlists found that would flip for your target after fees and shipping (with max bid for auctions), **Saved** keeps the ones you're tracking, and **Market** is the shared deal finder (`WATCHED_PLAYERS`). Manage watchlists (list icon). Settings (gear icon, also on Show) opens as a sheet over the current screen: profit target, grading fees, push alerts. The tab shows a badge for new alerts.
- **Collection:** portfolio summary and card grid. A card shows its live price, market trend, grading ROI, eBay listings, and P&L fields (grading cost, shipping you paid, where you bought it). **List on eBay** builds a listing kit (title, item specifics, description, Buy It Now / Best Offer pricing with break-even) to copy into eBay. **Profit & loss** has realized and unrealized profit, a 12-month chart, this week's movers and a CSV export.

The app talks to `CardScout.Api` (in the repo root). All commands below are **Windows PowerShell**.

---

## 1. Start the API

Run these once, from the repo root. They set the API's secrets with user-secrets, which are stored in your user profile and never committed:

```powershell
cd C:\Users\ryanc\source\repos\CardScout
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..." --project CardScout.Api
dotnet user-secrets set "ConnectionStrings:Oracle" "User Id=...;Password=...;Data Source=..." --project CardScout.Api
dotnet user-secrets set "SportsCardsPro:Token" "..." --project CardScout.Api
dotnet user-secrets list --project CardScout.Api   # check (prints values, so don't screen-share this)
```

The Claude model and effort level are in `CardScout.Api/appsettings.json` (`Anthropic:Model`, `Anthropic:Effort`).

Start the API. In Development it listens on all network interfaces, on port 5090:

```powershell
dotnet run --project CardScout.Api --launch-profile http
```

Check it from the PC with `curl http://localhost:5090/`. It should return `{"service":"CardScout.Api","status":"ok"}`.

### Users and API keys

Each person has their own row in `CARD_USERS` and their own API key. Every card and image belongs to a user, and the API only ever returns the caller's own data. To add a user:

```powershell
dotnet run --project CardScout.Api -- create-user --name "Jane" --email jane@example.com
```

This prints the new key **once**. Only its hash is stored, so if a key is lost, create a new user or put a new hash in `CARD_USERS.API_KEY_HASH`. Your user (id 1) already exists, and its key is already in `mobile/.env`.

## 2. Let your phone reach the API

Your phone can't use `localhost`, because on the phone that means the phone itself. Use your PC's LAN IP instead.

**Find your LAN IP:**

```powershell
Get-NetIPAddress -AddressFamily IPv4 |
  Where-Object { $_.InterfaceAlias -notmatch 'Loopback|vEthernet|WSL|Bluetooth' } |
  Select-Object InterfaceAlias, IPAddress
```

Use the Wi-Fi or Ethernet address, e.g. `192.168.1.72`. The phone must be on the same Wi-Fi.

**Open the firewall port.** Run this once, in PowerShell **as Administrator**:

```powershell
New-NetFirewallRule -DisplayName "CardScout API (dev)" -Direction Inbound -Protocol TCP -LocalPort 5090 -Action Allow -Profile Private
```

Your Wi-Fi network needs to be set to **Private** in Windows (Settings → Network & internet → Wi-Fi → your network). To remove the rule later: `Remove-NetFirewallRule -DisplayName "CardScout API (dev)"`.

Test it by opening `http://<your-ip>:5090/` in Safari on the phone. You should see the same `ok` JSON.

## 3. Configure the app

```powershell
cd C:\Users\ryanc\source\repos\CardScout\mobile
Copy-Item .env.example .env   # skip if .env already exists
notepad .env
```

```
EXPO_PUBLIC_API_URL=http://192.168.1.72:5090
EXPO_PUBLIC_API_KEY=<your key from create-user>
```

`.env` is gitignored. Note that `EXPO_PUBLIC_*` values are baked into the JavaScript bundle. That's fine for personal use, but before other people use the app, switch to real sign-in (see "Going multi-user" below) rather than shipping a key in the app.

## 4. Run the app

```powershell
npm install
npx expo start
```

Install **Expo Go** from the App Store, then scan the QR code with the iPhone Camera app. Allow camera access when asked.

After changing `.env`, restart Expo with the cache cleared: `npx expo start -c`.

**If the phone can't load the app** (the QR scans but it hangs or times out), the PC↔phone LAN connection is probably blocked. Use a tunnel instead:

```powershell
npx expo start --tunnel
```

The tunnel only carries the **app bundle**, not API calls. If the app loads but every request says "Can't reach the API", recheck step 2: the firewall rule, the network being Private, and the IP in `.env`.

## Checks

```powershell
npx tsc --noEmit      # typecheck
npx expo-doctor       # dependency/config check
```

## Project layout

```
src/app/                Expo Router screens
  _layout.tsx           root stack: providers, fonts, notification taps, keyboard Done bar
  settings.tsx          Settings, a modal over any tab
  (tabs)/_layout.tsx    tab bar + Deals alert badge
  (tabs)/index.tsx      Scan tab
  (tabs)/show/          Show tab (quick scan list + lot analyzer)
  (tabs)/deals/         Deals tab (alerts/saved/market, watchlists, watchlist/[id])
  (tabs)/inventory/     Collection tab (list, [id] detail, pnl, listing/[id] kit)
src/components/         camera, card form, price/verdict/grading/market panels, UI primitives
src/lib/                API client, flip + grading math (flip.ts mirrors Core/Flip/FlipAnalyzer.cs),
                        settings (saved to a JSON file), push notifications, image resize, theme
```

## Going multi-user (when the app goes live)

The data model is already per-user: `CARD_USERS` holds the users, `CARD_INVENTORY.USER_ID` links each card to its owner, every query filters by user, and images are stored under `users/<id>/`. What's still single-user is **how people sign in**, since right now everyone pastes an API key into `.env`. To go live:

1. Add real sign-in, e.g. Sign in with Apple via `expo-apple-authentication`, or an OAuth provider via `expo-auth-session`.
2. In the API, replace `ApiKeyMiddleware` with JWT validation that maps the token's issuer and subject to `CARD_USERS.AUTH_PROVIDER` / `AUTH_SUBJECT`, creating the user on first sign-in, and calls `CurrentUser.Set`. The endpoints don't need to change.
3. Store the session token with `expo-secure-store` instead of `EXPO_PUBLIC_API_KEY`.
4. Host the API over HTTPS, and move images to blob storage with a new `IImageStorage` implementation.
5. Add per-user rate limits on `/api/cards/identify`, since each call costs Anthropic credits.

## Push alerts

Watchlist alerts and "your card moved" alerts are sent by the worker (`dotnet run --project CardScout`) through Expo's push service. To turn them on:

1. Run `npx eas-cli@latest init` in `mobile/` once. It creates the EAS project and adds `extra.eas.projectId` to `app.json`; push tokens need it.
2. Restart Expo, open **Settings** (gear icon on the Deals tab), tap **Turn on & send a test alert** and allow notifications.

On Android, Expo Go can't receive remote push, so you need a development build (`npx eas-cli@latest build --profile development --platform android`). If push isn't set up, alerts still show in the Deals tab, and the tab badge counts new ones.

## Not built yet

- Posting listings straight to eBay. That needs the eBay Sell API with per-user OAuth consent. For now the listing kit gives you every field to copy, and opens eBay's listing page.
- Sold-price history. The eBay Browse API only returns active listings, so "On eBay now" shows asking prices. Comps come from SportsCardsPro, which is based on sales.
