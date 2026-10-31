# CardScout Mobile

Expo (SDK 58) app for photographing cards, identifying them with Claude, and tracking them in your CardScout inventory. It runs in **Expo Go** on iPhone. There's no custom native code, so EAS cloud builds work later without a Mac.

- **Scan:** front photo, then back photo (tap either to retake), then **Identify & price**. You get the market value for the card's grade, the full grade ladder (tap a tier to use that price), net after eBay fees, and profit against your cost. If you fix a field, tap **Re-price**. If the match is wrong, use **Wrong card?** to pick another comp. **Buying it?** takes an asking price and gives a buy/fair/pass verdict, **Grade it?** shows grading ROI per service (from Claude's read of centering/corners, or a preset), and **On eBay now** lists the cheapest active listings. Then **Add to collection**.
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

## Sign-in and accounts

The app opens on a sign-in screen. Email + password works with no extra setup. **Use dev API key** (shown when `EXPO_PUBLIC_API_KEY` is set in `.env`) signs in as the API-key user, as before. **Settings → Account** has Sign out and Delete account. The API needs `Auth:JwtSigningKey` in user-secrets:

```powershell
$b = New-Object byte[] 64; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
dotnet user-secrets set "Auth:JwtSigningKey" ([Convert]::ToBase64String($b)) --project CardScout.Api
```

The Apple, Google, eBay and SSO buttons appear only when their settings are present (`GET /api/auth/providers`). Google, eBay and SSO run in the browser through the API, so the provider has to reach the API's callback over **https**. Before setting them up, expose the API (a tunnel such as `cloudflared tunnel --url http://localhost:5090`, or a deployed host) and set:

```powershell
dotnet user-secrets set "Auth:PublicBaseUrl" "https://<your-public-host>" --project CardScout.Api
```

**Sign in with Apple** (works in Expo Go on an iPhone). In Development, `Auth:Apple:Audiences` already contains `host.exp.Exponent` (Expo Go's bundle id), so the button works with no Apple setup. For your own build: enroll in the Apple Developer Program (paid), enable the *Sign in with Apple* capability on your App ID (EAS does this from `ios.usesAppleSignIn` in `app.json`), then set `Auth:Apple:Audiences:0` to your bundle id (e.g. `com.yourname.cardscout`).

**Google.**
1. Google Cloud Console → APIs & Services → OAuth consent screen: create the app (External) and add the scopes `openid`, `email` and `profile`.
2. Credentials → Create OAuth client ID → **Web application**. Add the authorized redirect URI `https://<your-public-host>/api/auth/google/callback`.
3. Set the keys:
   ```powershell
   dotnet user-secrets set "Auth:Google:WebClientId" "<id>.apps.googleusercontent.com" --project CardScout.Api
   dotnet user-secrets set "Auth:Google:WebClientSecret" "<secret>" --project CardScout.Api
   ```
4. Optional, for a future native Google Sign-In build: create iOS/Android client ids and add them as `Auth:Google:ClientIds:0`, `:1`, and so on. `POST /api/auth/google` accepts ID tokens for those audiences.

**eBay** (uses the existing `Ebay:AppId` / `Ebay:CertId`).
1. developer.ebay.com → Application Keys → *User Tokens* → *Get a Token from eBay via Your Application* → add an eBay Redirect URL. Set **Your auth accepted URL** (and the declined URL) to `https://<your-public-host>/api/auth/ebay/callback`, and choose **OAuth**.
2. Copy the generated **RuName** and set it: `dotnet user-secrets set "Auth:Ebay:RuName" "<RuName>" --project CardScout.Api`. To use sandbox keys, also set `Auth:Ebay:Environment` to `sandbox`.

The API asks only for `commerce.identity.readonly` and reads the eBay `userId`/`username` from the Commerce Identity API. eBay doesn't vouch for the email, so eBay sign-ins never merge into an existing account.

**Company SSO (any OpenID Connect provider: Entra ID, Okta, Auth0, Google Workspace…).** Register a web/confidential app with the redirect URI `https://<your-public-host>/api/auth/oidc/callback`, then set:
```powershell
dotnet user-secrets set "Auth:Oidc:Authority" "https://login.microsoftonline.com/<tenant>/v2.0" --project CardScout.Api
dotnet user-secrets set "Auth:Oidc:ClientId" "<client id>" --project CardScout.Api
dotnet user-secrets set "Auth:Oidc:ClientSecret" "<secret>" --project CardScout.Api   # omit for a public client (PKCE only)
dotnet user-secrets set "Auth:Oidc:DisplayName" "Acme SSO" --project CardScout.Api
dotnet user-secrets set "Auth:Oidc:Domains:0" "acme.com" --project CardScout.Api      # optional: asks for a work email first
```

**Password reset emails.** There's no email provider yet. In Development, the reset link and code are written to the API log (`DEV EMAIL (not sent)`). The link opens `cardscout://reset-password?token=…`, which Expo Go can't handle, so in Expo Go use **I have a reset code** and paste the code. Before launch, implement `IEmailSender` (`CardScout.Api/Auth/EmailSender.cs`) with a real provider.

Still to do before going live: host the API over HTTPS, move images to blob storage (a new `IImageStorage`), and add per-user limits on `/api/cards/identify`.

## Push alerts

Watchlist alerts and "your card moved" alerts are sent by the worker (`dotnet run --project CardScout`) through Expo's push service. To turn them on:

1. Run `npx eas-cli@latest init` in `mobile/` once. It creates the EAS project and adds `extra.eas.projectId` to `app.json`; push tokens need it.
2. Restart Expo, open **Settings** (gear icon on the Deals tab), tap **Turn on & send a test alert** and allow notifications.

On Android, Expo Go can't receive remote push, so you need a development build (`npx eas-cli@latest build --profile development --platform android`). If push isn't set up, alerts still show in the Deals tab, and the tab badge counts new ones.

## Not built yet

- Posting listings straight to eBay. That needs the eBay Sell API with per-user OAuth consent. For now the listing kit gives you every field to copy, and opens eBay's listing page.
- Sold-price history. The eBay Browse API only returns active listings, so "On eBay now" shows asking prices. Comps come from SportsCardsPro, which is based on sales.
