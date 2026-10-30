# CardScout Mobile

Expo (SDK 58) app for photographing cards, identifying them with Claude, and tracking them in your CardScout inventory. It runs in **Expo Go** on iPhone. There's no custom native code, so EAS cloud builds work later without a Mac.

- **Scan:** front photo, then back photo (tap either to retake), then **Identify & price**. You get the market value for the card's grade, the full grade ladder (tap a tier to use that price), net after eBay fees, and profit against your cost. If you fix a field, tap **Re-price**. If the match is wrong, use **Wrong card?** to pick another comp. Then **Add to collection**.
- **Collection:** portfolio summary (market value, cost, realized profit) and a card grid with a status filter. Opening a card fetches a live price for its saved comp.
- **Deals:** placeholder for phase 2 (underpriced eBay listings and push alerts).

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
  _layout.tsx           tabs + React Query provider
  index.tsx             Scan tab
  inventory/            Inventory tab (list + [id] detail, in a stack)
  deals.tsx             Deals placeholder
src/components/         camera, card form, UI primitives
src/lib/                API client, config, image resize, theme
```

## Going multi-user (when the app goes live)

The data model is already per-user: `CARD_USERS` holds the users, `CARD_INVENTORY.USER_ID` links each card to its owner, every query filters by user, and images are stored under `users/<id>/`. What's still single-user is **how people sign in**, since right now everyone pastes an API key into `.env`. To go live:

1. Add real sign-in, e.g. Sign in with Apple via `expo-apple-authentication`, or an OAuth provider via `expo-auth-session`.
2. In the API, replace `ApiKeyMiddleware` with JWT validation that maps the token's issuer and subject to `CARD_USERS.AUTH_PROVIDER` / `AUTH_SUBJECT`, creating the user on first sign-in, and calls `CurrentUser.Set`. The endpoints don't need to change.
3. Store the session token with `expo-secure-store` instead of `EXPO_PUBLIC_API_KEY`.
4. Host the API over HTTPS, and move images to blob storage with a new `IImageStorage` implementation.
5. Add per-user rate limits on `/api/cards/identify`, since each call costs Anthropic credits.

## Phase 2 notes

Push notifications need an **EAS development build**, because Expo Go doesn't support remote push. You can build that in the cloud from Windows with `npx eas-cli@latest build --profile development --platform ios`.
