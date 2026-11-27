# GameRebellion

[Documentation](https://docs.gamerebellion.com/) · [Unity SDK](https://github.com/gamerebellion/gamerebellion-unity-sdk) · [S2S API](https://docs.gamerebellion.com/sdk/sdk-integrations/server-to-server)

Game analytics SDK that tracks in-game events and contextualizes your data against **400,000+ titles** across the gaming industry — progression benchmarks, monetization baselines, genre comparisons, and real-time dashboards.

#### Initialize and track events in your game

```csharp
using GameRebellion.Unity;

public class GRBootstrap : MonoBehaviour
{
    void Awake() => GameRebellion.Initialize("YOUR_API_KEY");
}
```

```csharp
// Track a level completion with progression context
GameRebellion.TrackProgression(new GrProgressionEvent
{
    Type = "level",
    Status = "complete",
    Progression01 = "world_1",
    Progression02 = "level_5",
    Score = 4200
});

// Track a purchase
GameRebellion.TrackTransaction(new GrTransactionEvent
{
    ProductId = "gem_pack_500",
    Amount = 4.99,
    Currency = "USD"
});

// Track any custom event
GameRebellion.TrackEvent("boss_defeated", "{\"boss\":\"dragon\",\"time_sec\":142}");
```

---

## In-Game Events

26 event types across 6 categories. Track what matters, ignore what doesn't.

| Category | Events | Example |
| --- | --- | --- |
| **Session** | session_start · session_stop · login · logout | Automatic lifecycle tracking |
| **Progression** | progression · level_up · achievement | Player journey and milestones |
| **Monetization** | transaction · crypto_transaction | IAP, subscriptions, crypto payments |
| **Marketing** | impression · click · conversion · creator_code_use | Attribution and campaign tracking |
| **Social** | friend_invite · group_join · chat_message · voice_call | Community and multiplayer signals |
| **Technical** | FPS · memory · log · health | Performance and stability metrics |

Custom events via `GameRebellion.TrackEvent("event_name", jsonPayload)` for anything not covered above.

---

## Integrations

| Engine / Method | Status | Package |
| --- | --- | --- |
| **Unity** | Available | [com.gamerebellion.sdk](https://github.com/gamerebellion/gamerebellion-unity-sdk) via UPM or `.unitypackage` |
| **Server-to-Server** | Available | [REST API](https://docs.gamerebellion.com/sdk/sdk-integrations/server-to-server) — same JSON envelope as SDK (`properties`, `identifiers`, `utms`). Concordance: [S2S vs SDK matrix](https://docs.gamerebellion.com/sdk/reference/s2s-sdk-concordance) |
| **Unreal Engine** | In development | UE5 C++ plugin |
| **Godot · Native Mobile · Web · Flutter** | Planned | Use S2S API in the meantime |

---

## Quick Start

**1. Install** — Add via Unity Package Manager or import the `.unitypackage`

**2. Initialize** — One call in your first scene

```csharp
GameRebellion.Initialize("YOUR_API_KEY");
```

**3. Track** — Start sending events

```csharp
GameRebellion.TrackProgression(new GrProgressionEvent
{
    Type = "level",
    Status = "complete",
    Progression01 = "world_1",
    Score = 4200
});
```

---

## Configuration

The SDK reads its settings from the `GameRebellionSettings` asset bundled with the package.
Defaults are production-ready — most games only ever call `Initialize(apiKey)`.

> Installed via UPM (git URL)? The bundled asset is read-only — use the `Initialize`
> overloads below to change environment or logging. Installed via `.unitypackage`?
> You can also edit the asset directly in the Inspector.

### Environments

The SDK targets **Production** by default. Point QA or internal builds at Staging
without touching the asset:

```csharp
// Shipped game — Production (asset default)
GameRebellion.Initialize("YOUR_API_KEY");

// QA / internal build
GameRebellion.Initialize("YOUR_API_KEY", GameRebellion.GrEnvironment.Staging);
```

### Debug logging

Verbose SDK logs are on automatically for Staging and Development. To get the same
logs in a Production build (e.g. diagnosing an integration issue), pass `isDebug`:

```csharp
GameRebellion.Initialize("YOUR_API_KEY", GameRebellion.GrEnvironment.Production, isDebug: true);
```

Keep `isDebug` off in release builds.

### Settings reference

| Setting | Default | What it does |
| --- | --- | --- |
| `GameVersion` | *(empty)* | Version reported with events. Empty = `Application.version` from Player Settings. |
| `BuildNumber` | *(empty)* | Build identifier reported with events. Empty = the platform's own build counter (Android `versionCode`, iOS `CFBundleVersion`), or `Application.buildGUID` on desktop. |
| `Environment` | `Production` | Backend the SDK talks to. Overridable via `Initialize(apiKey, environment)`. |
| `BatchSizeBytes` | `65536` | Max size of one event batch before sending. Clamped to 1 KB – 1 MB. |
| `BatchMaxEvents` | `100` | Max events per batch before sending. Clamped to 1 – 1000. |
| `FlushIntervalMs` | `30000` | How often buffered events are flushed even if batch limits aren't reached. Clamped to 1 s – 5 min. |
| `IsDebug` | `false` | Verbose SDK logs in any environment. Overridable via `Initialize(apiKey, environment, isDebug)`. |
| `SessionResumeGraceSeconds` | `30` | How long the app may stay backgrounded before the session ends. See below. |
| `AutoTrackPurchases` | `false` | Report in-app purchases automatically, with no per-purchase game code. See below. |

Batching defaults are tuned for production; lowering them gives fresher data at the
cost of more network and battery use.

---

## Sessions and backgrounding

A session ends when the app has been in the background for longer than
`SessionResumeGraceSeconds` — **30 seconds** by default. Come back sooner and the
same session simply continues: no `session_stop`, no `session_start`, same session
id.

This matters more than it sounds. A purchase sheet, a rewarded ad, a screenshot
and a glance at a notification all background your app for a few seconds each.
Ending the session every time inflates your session count with fragments and drags
average session duration toward zero — a "session" that exists only because
somebody bought something is not a session.

Set it to `0` to end the session the moment the app is backgrounded.

A session interrupted and never returned to — the OS reclaimed the app, or it
crashed — still reports its `session_stop` on the next launch, timed to the moment
it was interrupted rather than to the recovery.

> **Changing this shifts your metrics.** Raising the grace period lowers session
> counts and raises average duration, so the day you change it is a discontinuity
> in every session-based chart. Worth doing deliberately rather than noticing later.

---

## Automatic purchase tracking

Tick **Auto-track purchases** in `GameRebellionSettings` and the SDK reports every
in-app purchase by itself. There is no per-purchase call to write and no IAP
library to integrate against — the SDK observes the platform store directly, so
purchases are seen whether your game uses Unity IAP, StoreKit by hand, or
anything else.

**Availability: iOS and iPadOS 15+ today.** Android and macOS are in development.
Everywhere else, and on older iOS, keep calling `TrackTransaction` yourself.

### What you get

Each purchase reports its product id, price, currency, the store's transaction id,
whether it was a sandbox purchase, and when it actually happened — so a purchase
restored at launch is booked on its original date rather than as today's revenue.

The payload is deliberately small. Everything else a purchase can say about itself
is reachable server-side from the transaction id, so the SDK does not carry a 2.6 KB
signed receipt on every sale to duplicate data the backend can already fetch.

The SDK never finishes or acknowledges a transaction. Your game keeps full
ownership of fulfilment; this only watches.

### Turning it on

1. Select your `GameRebellionSettings` asset and tick **Auto-track purchases**.
2. Rebuild. The setting drives a compile-time switch, so it takes effect on the
   next build rather than at runtime — with it off, none of the purchase-observing
   code is in your app at all.
3. Nothing else. `GameRebellion.Initialize` starts the observer.

For explicit control over when observation runs:

```csharp
GameRebellionIap.StartObservingTransactions();
GameRebellionIap.StopObservingTransactions();

if (GameRebellionIap.IsObserving) { /* ... */ }
```

These methods always compile, whether or not the setting is on, so unticking the
box never breaks your build.

### What it does not see

A store observer only ever sees purchases that reached the store. Declined cards
and players backing out of the purchase sheet are invisible to it — if you want
those reported, call `TrackTransaction` with `Status = "failed"` from your own
failure handler. Refunds and subscription renewals happen while your game is not
running and are reconciled server-side.

### Reporting purchases yourself

Nothing changes. `GameRebellion.TrackTransaction` works exactly as before, is
unaffected by the setting, and is still the right call for stores the SDK cannot
observe (Steam, Epic, consoles) and for in-game currency spends.

### Shipping on more than one platform

Auto-tracking covers iOS today, so a game that also ships on Android still needs
to report purchases itself there — otherwise that revenue goes unrecorded. Branch
on `IsObserving` rather than on the platform:

```csharp
void OnPurchaseComplete(bool succeeded, Product product, string failureReason)
{
    // Where the SDK observes the store, stay out of its way.
    if (GameRebellionIap.IsObserving)
        return;

    GameRebellion.TrackTransaction(new GrTransactionEvent
    {
        ProductId = product.definition.id,
        Amount = (double)product.metadata.localizedPrice,
        Currency = product.metadata.isoCurrencyCode,
        Status = succeeded ? "Success" : "Fail",
        FailureReason = failureReason
    });
}
```

`IsObserving` is false whenever nothing is auto-reporting — on a platform with no
observer, on an OS too old to run one, and when auto-tracking is switched off. So
this one branch stays correct without your code testing for platforms or SDK
versions, and it stops double-reporting by itself as more platforms gain
observers.

#### If you want failure and cancellation data

Declined cards and cancelled purchase sheets are **never** reported automatically,
on any platform — a store observer only ever sees purchases that reached the
store. The branch above therefore records no failures on iOS.

If that data matters to you, report failures yourself regardless of `IsObserving`,
and let the observer keep the successes:

```csharp
if (!succeeded)
{
    GameRebellion.TrackTransaction(new GrTransactionEvent { /* ... */ Status = "Fail" });
    return;
}

if (GameRebellionIap.IsObserving)
    return;

GameRebellion.TrackTransaction(new GrTransactionEvent { /* ... */ Status = "Success" });
```

This cannot double-count: a failure carries no store transaction id, so it shares
no identity with any successful purchase the observer reports.

---

Full integration guide at **[docs.gamerebellion.com](https://docs.gamerebellion.com)**.
