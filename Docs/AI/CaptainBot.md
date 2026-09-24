# Captain Bot

Owner: S4 · Architecture: Goal prediction + Dijkstra fields + A* intercept

## Role in gameplay

The Captain is the boss of the Control Room area. It does not chase the player. It watches where the player is going, predicts which objective they are heading for, and gets to a point on their route first.

- **Candidate goals:** the active control switches (up to 3), the Control Room door, and battery pickups when the player's ammo is below 30%.
- **Threat:** the player meets the Captain in front of them, at a chokepoint, rather than being followed from behind.
- **Pacing:** the Captain only commits to an ambush when it is confident. Otherwise it keeps its distance and observes, so it never feels like it is cheating.

## Creative hook

**The gameplay problem:** a boss that chases is predictable. The player always knows where it is (behind them) and can beat it by simply outrunning it or looping around cover.

**The Captain's solution: it predicts where you are going, not where you are.** Every other enemy reacts to the player's position. The Captain reasons about the player's *intention*:

1. It compares the route the player has taken over the last 5 seconds with the shortest route to each goal. A player who stays on the shortest route to Switch 2 is probably going to Switch 2.
2. It works out which chokepoints on that predicted route it can reach at least 1 second before the player.
3. It walks there and waits, facing the way the player will come.

**Why it is surprising to play against:** the player expects the enemy behind them and instead finds it waiting in front of them. Its tactics also change with its confidence: it shadows the player while unsure, and commits to an ambush once the prediction is strong.

**Counter-play:** the prediction is based on shortest routes, so a player who takes an unexpected route lowers the Captain's confidence and can slip past it. The Captain is beatable by a player who realises it is reading their movement.

**Team play:** the Captain publishes its predicted goal to the shared blackboard at 2 Hz. The Saboteur uses it to measure which door closure forces the longest detour (falling back to the nearest active switch if no prediction exists). Nice-to-have: the Tracker investigates the predicted goal when it has nothing to hear.

## Architecture

The Captain is a finite-state machine built on the shared FSM framework: each state is an object with `Enter`, `Tick` and `Exit`, and transitions are data (condition + priority + target), not a `switch` statement. Every tick the machine takes the highest-priority transition whose condition is true.

**Confidence** below means the probability of the most likely goal, `P(g*)`, from goal inference (see Maths to defend). It is recomputed at 2 Hz.

### States

| State | What the Captain does |
| --- | --- |
| **Observe** | Prediction is too uncertain to commit. Keeps its distance from the player and stays out of sight while the prediction updates. |
| **Intercept** | Confident about `g*`. Picks the first chokepoint on the player's predicted route it can reach at least 1 s before them, and walks there with A*. If no cell qualifies, it heads to `g*` itself to defend it. |
| **Ambush** | At the intercept cell. Stands still, facing the direction the player will arrive from. |
| **Engage** | Player is in view within 10 m. Faces the player and fires hitscan shots, each with a 0.3 s wind-up telegraph. |
| **Reassess** | Something invalidated the plan. Discards the current intercept cell, re-runs goal inference immediately, then hands over to Observe or Intercept. Lasts one decision tick. |
| **Stunned** | Hit points reached 0. Falls apart for the stun duration, releases its reserved cell, then goes to Reassess. |

### Transitions

Higher priority wins when several conditions are true on the same tick.

| From | To | Condition | Priority |
| --- | --- | --- | --- |
| Any | Stunned | Hit points reach 0 | 100 |
| Stunned | Reassess | Stun timer ends | 90 |
| Observe, Intercept, Ambush | Engage | Player visible within 10 m | 80 |
| Engage | Reassess | Player no longer visible | 70 |
| Intercept, Ambush | Reassess | `g*` changes, confidence drops below 0.5, the player reaches `g*`, or the intercept cell becomes blocked or no longer satisfies the 1 s margin | 60 |
| Intercept | Ambush | Captain reaches the intercept cell | 50 |
| Reassess | Intercept | Confidence ≥ 0.5 | 40 |
| Reassess | Observe | Confidence < 0.5 | 30 |
| Observe | Intercept | Confidence ≥ 0.5 | 20 |

```text
            confidence ≥ 0.5              reached cell
  Observe ───────────────────▶ Intercept ─────────────▶ Ambush
     ▲                            │  ▲                    │
     │ confidence < 0.5           │  │ confidence ≥ 0.5   │ goal changed /
     │                            ▼  │                    │ confidence dropped
     └──────────────────────── Reassess ◀─────────────────┘
                                  ▲
         player lost from view    │        stun ends
  Engage ─────────────────────────┴──────────────────── Stunned
     ▲
     └── any of Observe / Intercept / Ambush: player visible within 10 m
```

## Why this architecture over the alternatives

| Alternative | Why not |
| --- | --- |

## Maths to defend
<!-- Heuristics, weights, curves, formulas, with the reasoning for each value -->

## Edge cases

| Case | Handling | Test |
| --- | --- | --- |

## Tests
<!-- EditMode tests and what each proves -->

## Measured results
<!-- Numbers from AIPerformanceLog.md -->
