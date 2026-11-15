# MistChess Defense Guide

## Suggested 15–20 minute flow

| Time | Part | What to show |
| --- | --- | --- |
| 0–2 min | Team, topic, aim | Introduce Sansyzbay Dossymzhan and Meiram Dossym, then explain Fog of War Chess and the goal of building a playable, privacy-aware implementation. |
| 2–5 min | Existing technology and comparison | Explain C#/.NET 8, Blazor Interactive Server, and the public Chess.com help-page comparison. Clarify that “not described” refers only to the cited articles. |
| 5–8 min | Architecture | Trace a move from the UI to `GameSessionStore` or `OnlineGameClient`, through `MistChess.Core`, then back as a player-specific board view. |
| 8–10 min | Practical value | Show King Radar, Acoustic Footsteps, and replay from White Fog, Black Fog, and omniscient perspectives. |
| 10–14 min | Live demo | Start a bot game, make a move, show the bot response and fog boundary, then create and join an online room in a second browser session and exchange a legal move. |
| 14–16 min | Method and evidence | Run the tests or show the latest verified result: 67 passed, 0 failed. Explain the focused desktop, two-session online, and 390 px viewport smoke checks. |
| 16–20 min | Questions | Both team members should be ready to explain the core rules, fog projection, UI state, and online room validation. |

## Live demo commands

From the repository directory in PowerShell:

```powershell
dotnet run --project .\src\MistChess.Web\MistChess.Web.csproj
```

Open the local URL printed by .NET. The included launch profile uses `http://localhost:5254`.

1. On **Game**, start a Fog of War game as White against Mist Walker.
2. Select the pawn on e2 and move it to e4. Point out that destinations are shown only when visible, then wait for the bot response.
3. Use **Undo**, then open **Replay** and switch between White Fog, Black Fog, and omniscient views.
4. Open **Online** in one browser session, create a room, and copy its code. Join from another browser profile or a second device on the same host, then exchange a move.
5. Show **Rules** and the `/health` endpoint if time allows.

To repeat the automated checks:

```powershell
dotnet test .\MistChess.sln
```

The last verified run used .NET SDK 8.0.428 and passed 67 tests. The PowerShell image in the presentation is an unmodified earlier run showing 60 tests; it is labeled as historical evidence. The current 67-test result was checked separately in the terminal.

## Questions to rehearse

### Why separate the game engine from Blazor?

`MistChess.Core` contains the immutable board, move rules, fog projection, bot decisions, and replay. It does not depend on UI or network APIs, so the same rules are used by local games, online rooms, and automated tests.

### What does `Result<T, E>` add?

Move application can return either a new `BoardState` or a typed `IllegalMoveError`. Callers must handle the success or error case instead of treating an invalid move as an exception-driven control flow.

### How does Fog of War prevent information leakage?

`FogEngine` projects the authoritative board into a `MaskedBoardState` for a specific player. Online clients receive square visibility and visible piece data for their own view; the room service does not return the full board or legal-move list.

### What is King Radar?

It reveals a five-by-five area around the player's king, giving a local reference point in the otherwise partial board view.

### What are Acoustic Footsteps?

When the rules allow it, a recent opposing move near the player's visible area leaves a ripple. It indicates movement without identifying the hidden piece.

### What do the five bots do?

They use different evaluation styles and search depths. Their choices are based on a fog projection and legal moves, not on hidden opponent piece locations.

### How are online moves validated?

`OnlineGameRegistry` checks room membership, whose turn it is, and the legal move set against the server-held board before applying a move. Each participant then receives a separately projected view.

### What happens if the server restarts?

Current online rooms are held in memory and are lost when the host restarts. Persistent storage, authentication, and production abuse controls are outside this classroom demo's implemented scope.

### What has and has not been tested?

The xUnit suite has 67 tests. A separate manual browser check exercised a bot move, two isolated online sessions exchanging moves, and a 390 px responsive viewport. A full device/browser matrix, automated browser test suite, code coverage report, and production load test have not been completed.

## Keep the claims precise

- The source is available at [github.com/dosiks2028/mistchess-fog-of-war](https://github.com/dosiks2028/mistchess-fog-of-war).
- There is no public hosted game URL yet; the current way to play is to run the app locally.
- The comparison describes only the cited public Chess.com help articles, not Chess.com's private implementation.
- Do not describe the room registry as persistent or multi-server. It is currently in-memory and single-process.
- Be prepared for either author to answer questions about any project area.
