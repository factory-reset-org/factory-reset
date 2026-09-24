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
<!-- States / actions, transition table or scoring, diagram -->

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
