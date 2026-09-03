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
| **Server-to-Server** | Available | [REST API](https://docs.gamerebellion.com/sdk/sdk-integrations/server-to-server) — any language, any engine |
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

Batching defaults are tuned for production; lowering them gives fresher data at the
cost of more network and battery use.

---

Full integration guide at **[docs.gamerebellion.com](https://docs.gamerebellion.com)**.
