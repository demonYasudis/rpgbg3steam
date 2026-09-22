# AGENTS.md

## 1. Purpose of this file

This file is the main working contract for AI coding agents (especially Codex) working in this repository.

Before changing code:

1. Read this file completely.
2. Inspect the current repository and Unity project structure.
3. Identify what already exists and reuse it when reasonable.
4. Work only on the current requested work package.
5. Do not silently expand scope.
6. Do not start the next work package automatically.
7. After finishing, compile/check the affected code and report:
   - what changed;
   - what files were added/modified;
   - what was tested;
   - what remains;
   - any risks or technical debt.

The project owner is a C# programmer. Prefer clear, maintainable C# over clever abstractions.

---

# 2. Project summary

## Working concept

A dark pixel-art, turn-based tactical RPG for Steam made primarily by one developer.

The player is not a single hero. The player manages an adventurers' guild, hires/controls adventurers, builds a party of four, sends them on expeditions, gains loot and experience, and may lose characters permanently.

Main inspirations:

- Baldur's Gate 3 — visible dice, tactical decisions, randomness.
- Battle for Wesnoth — hexagonal maps, readable pixel-art tactics.
- Pathfinder CRPGs — world travel and random encounters while moving between locations.
- Darkest Dungeon — oppressive mood, dangerous expeditions, attachment to expendable adventurers.
- Chess — small battlefield, readable state, meaningful positioning.

This is NOT intended to reproduce D&D content or copyrighted classes/spells/lore.
Use original names, simplified rules and original content.

---

# 3. Core design pillars

Every gameplay feature should support at least one of these pillars.

## 3.1 Tactical clarity

- Small maps.
- Hexagonal cells.
- Few abilities with meaningful differences.
- Position matters.
- Fog of war matters.
- Minimal environmental interactions.
- Decisions should be understandable without reading a large rules manual.

## 3.2 Visible randomness

Randomness is a feature, not noise.

Use dice for:
- attacks;
- damage;
- selected checks;
- loot;
- encounters;
- procedural generation.

Important dice results should be visible to the player.

During an action, a short result may appear above the acting/target unit, for example:

`🎲 16 HIT`
`-7 HP`

A detailed combat log can be added later.

## 3.3 Dangerous expeditions

Characters can die.

Dead characters leave a body/corpse.
If the party brings the body back to the guild, resurrection may be possible for a cost.
If the body is abandoned, the character can be permanently lost.

For the first tactical prototype, corpse transport/resurrection can be deferred until the guild meta-loop exists.

## 3.4 Procedural replayability

The long-term game should support:
- procedural battle maps;
- random encounters;
- random loot;
- random traps/events/checks;
- repeatable dungeon-clearing expeditions;
- a living world outside the main story.

## 3.5 Extensible campaign platform

Long-term goal:
players/DMs should eventually be able to create and publish their own content.

Future direction:
`Create campaign -> map -> NPC -> dialogue -> encounter -> save -> publish`

This is NOT part of the first demo.

However, gameplay data should be reasonably data-driven so a future editor is possible.

---

# 4. Demo goal

The first Steam demo must prove one thing:

> A small procedural tactical expedition is fun enough that the player wants to play one more run.

The demo is NOT a miniature version of the final game.
It is a focused vertical slice.

## Demo gameplay loop

`Guild -> choose 4 adventurers -> choose expedition -> generated tactical map -> explore/fight -> loot -> extraction -> guild`

Early prototypes may skip the guild and begin directly in battle.

---

# 5. Hard scope for Demo v0.1

## Party

Exactly 4 controllable adventurers.

Initial classes:

1. Warrior
2. Rogue
3. Ranger
4. Mage

For the demo each class should have:

- 1 basic attack;
- 2 class abilities.

Do not implement large spell lists or deep skill trees.

### Suggested abilities

Warrior:
- Basic melee attack
- Heavy Strike
- Push

Rogue:
- Basic melee attack
- Backstab
- Smoke / Evade

Ranger:
- Basic ranged attack
- Aimed Shot
- Trap

Mage:
- Basic magic attack
- Fire Burst / Fireball
- Blink

Names and exact balance may change.

---

# 6. Combat rules

Use an original simplified tabletop-inspired ruleset.

Do not copy proprietary D&D text, spell descriptions, monsters or named content.

## Core stats

Keep the first version small.

Possible runtime stats:

- MaxHealth
- CurrentHealth
- Attack
- Defense
- DamageBonus
- Movement
- Initiative
- VisionRange

Optional later:
- Strength
- Agility
- Mind

Do not add stats without a gameplay need.

## Attack resolution

Suggested baseline:

`d20 + Attack >= Defense`

On hit:

`WeaponDie + DamageBonus`

Example:

`d20(13) + Attack(4) = 17`
`Target Defense = 15`
`HIT`

Damage:

`d8(5) + 2 = 7`

This formula is a project rule, not a commitment to D&D compatibility.

## Turn economy

Demo baseline:

- movement;
- one action;
- end turn.

Do NOT add bonus actions, reactions, concentration or complicated action economies in the first prototype.

## Initiative

Units act in an ordered turn queue.

Initial implementation can use:

`d20 + Initiative`

Exact initiative design can be changed after playtesting.

---

# 7. Battlefield

## Grid

- Hexagonal grid.
- Target battle size: 12 x 12 hexes.
- Use a single authoritative coordinate representation.
- Prefer axial coordinates `(q, r)` internally unless the existing project already uses a sound alternative.
- Centralize conversion between hex coordinates and Unity world positions.

Required grid capabilities:

- get cell by coordinate;
- get six neighbors;
- distance;
- reachable cells;
- pathfinding;
- occupied/unoccupied state;
- terrain type;
- movement cost;
- visibility state.

## Terrain

For demo:

- Ground
- HighGround
- Pit
- Blocked

Keep terrain rules deliberately minimal.

Possible initial rules:

- Ground: normal.
- HighGround: one level only; may provide a small ranged advantage later.
- Pit: cannot be normally occupied; units may be pushed into it and die/take severe damage.
- Blocked: cannot enter.

Do NOT implement multi-floor buildings for the demo.

## Environment

Minimal interactions only.

Do not build a general-purpose physics/environment-combo system yet.

---

# 8. Fog of war

Each hero has a VisionRange.

Cell visibility states:

- Unknown
- Explored
- Visible

Rules:

- Unknown cells are hidden.
- Explored cells show remembered terrain but not current enemies.
- Visible cells show current units and interactable objects.
- Enemy visibility is derived from party vision.

Start with a simple deterministic visibility solution.
Performance optimization can wait until needed.

---

# 9. Enemy AI

AI must be simple and reliable before it is clever.

Initial melee AI:

1. If an attack is available, attack a reasonable target.
2. Otherwise find the nearest visible/reachable hero.
3. Move toward the target.
4. Attack if possible after movement.
5. End turn.

Initial ranged AI:

1. Attack from range if possible.
2. Prefer not to stand adjacent to melee heroes if a safe reachable cell exists.
3. Otherwise behave similarly to melee AI.

Future archetypes:
- Aggressive
- Ranged
- Coward
- Support
- Boss

Do not implement complex utility AI for the demo unless explicitly requested.

---

# 10. Demo enemies

Target roughly 5 enemy archetypes + 1 mini-boss.

Placeholder examples:

- Rat
- Skeleton-like melee enemy (use an original final name/art)
- Goblin-like melee enemy (original final name/art)
- Archer
- Brute
- Caster
- Mini-boss

During prototyping primitive names/art are acceptable.
Before shipping, replace content that is too derivative.

---

# 11. Procedural dungeon

First demo biome: dark crypt/cellar/dungeon.

Target tactical area: 12 x 12 hexes.

Generator should use a deterministic seed.

Always expose/store the seed for debugging.

The generator may initially create:

1. entry/spawn area;
2. 3-5 connected regions/rooms;
3. corridors/openings;
4. blocked cells;
5. optional high ground;
6. optional pits;
7. enemies;
8. a chest/reward;
9. optional trap;
10. extraction/goal cell.

Generation must validate that required objectives are reachable.

Avoid sophisticated Wave Function Collapse or ML generation for the first demo.
Prefer a simple algorithm that is easy to debug.

---

# 12. Meta game / guild

The guild is the home screen between expeditions.

Demo target features:

- roster;
- party selection;
- gold;
- simple storage/inventory;
- healing;
- resurrection if body was recovered;
- expedition selection/start.

Do not make a walkable tavern for the first demo.
A strong pixel-art background plus UI is enough.

Future:
- guild upgrades;
- blacksmith;
- alchemist;
- library;
- larger roster;
- contracts/jobs board.

---

# 13. Death

Long-term target:

- At `HP <= 0`, character dies.
- A corpse remains.
- A surviving party may recover the corpse.
- Returning the corpse enables resurrection for a cost.
- Abandoning it can permanently remove the character.

For early combat prototypes, death can simply remove/disable the unit.
Corpse recovery is implemented later as a separate work package.

---

# 14. Loot / progression

Keep demo loot intentionally small.

Initial categories:

- weapon;
- armor;
- consumable;
- gold.

Example prototype item differences:

Weapon:
- attack modifier;
- damage die;
- optional one simple modifier.

Armor:
- defense modifier.

Consumable:
- heal;
- possibly one utility consumable later.

Avoid Diablo-like random affix complexity in v0.1.

---

# 17. Architecture principles

## 17.1 Unity + C#

Engine: Unity
Language: C#

Before introducing a dependency or package:
- check whether Unity/project already provides the needed functionality;
- avoid new production dependencies unless they clearly reduce project risk.

## 17.2 Separate game state from presentation

Where practical:

- domain/gameplay logic should not depend directly on sprites/animations;
- MonoBehaviours should not become giant state containers;
- visual components should react to gameplay state/events.

Avoid overengineering.
The goal is testable, understandable gameplay code.

## 17.3 Data-driven content

Classes, abilities, enemies and items should be representable as data.

For Unity, ScriptableObjects are acceptable for static definitions such as:

- UnitDefinition
- AbilityDefinition
- EnemyDefinition
- ItemDefinition
- TerrainDefinition

Runtime state must not mutate shared ScriptableObject assets.

Keep runtime instances separate from definitions.

## 17.4 Suggested high-level modules

Names may adapt to existing project conventions.

```text
Core/
    GameBootstrap
    Random/
    Save/

HexGrid/
    HexCoordinates
    HexCell
    HexGrid
    HexPathfinder

Combat/
    Combatant
    Stats
    TurnManager
    CombatSystem
    DamageSystem
    AbilitySystem

Abilities/
    AbilityDefinition
    AbilityContext
    AbilityExecutor

AI/
    EnemyBrain
    MeleeBrain
    RangedBrain

Visibility/
    FogOfWarSystem
    VisionSystem

Generation/
    DungeonGenerator
    DungeonGenerationConfig
    DungeonValidator

Units/
    UnitDefinition
    UnitRuntimeState
    UnitView
    UnitController

UI/
    TurnOrderUI
    UnitPanel
    CombatText
    ActionBar

Meta/
    GuildState
    Roster
    Inventory
    Expedition
```

Do not create every directory/class immediately.
Create modules only when the current work package needs them.

---

# 18. Coding rules

Prefer:

- small focused classes;
- explicit names;
- composition over deep inheritance;
- readonly where useful;
- enums for small stable state sets;
- interfaces only where they improve testing/substitution;
- pure C# classes for algorithms when Unity API is unnecessary;
- clear public APIs;
- defensive validation at system boundaries.

Avoid:

- giant manager classes;
- global static mutable state;
- hidden FindObjectOfType dependencies;
- excessive singletons;
- reflection-driven gameplay;
- premature ECS conversion;
- premature optimization;
- abstractions with only one hypothetical future use.

If a temporary shortcut is necessary, mark it clearly:

`// TODO(DEMO): ...`

Do not leave unexplained magic numbers when a named constant/config is appropriate.

---

# 19. Repository behavior for Codex

For every task:

## Before implementation

1. Inspect relevant files.
2. Look for existing conventions.
3. Check Unity version from project files if available.
4. Check whether tests already exist.
5. Identify scene/prefab dependencies before changing serialized fields.
6. State a short implementation plan internally and then implement.

## During implementation

- Keep changes focused.
- Avoid unrelated formatting/restructuring.
- Preserve existing public APIs unless the task requires change.
- Do not delete working systems just to replace them with a preferred architecture.
- Do not mass-rename assets without a strong reason.
- Be careful with Unity `.meta` files.
- Do not hand-edit generated Unity caches (`Library`, `Temp`, etc.).

## After implementation

Run the strongest checks available in the repository.

At minimum:
- ensure C# compiles if an available command/tool can verify it;
- run EditMode/PlayMode tests if relevant and available;
- inspect changed files for obvious serialization/API errors.

If Unity cannot be launched in the current environment, explicitly say so and perform all available static checks.

Never claim a scene was visually tested if it was not.

---

# 20. Git behavior

Unless the user explicitly asks:

- do not push;
- do not force-push;
- do not rewrite history;
- do not modify secrets;
- do not create releases;
- do not merge branches.

Commits are allowed only if explicitly requested.

Keep generated files and Unity caches out of version control according to `.gitignore`.

---

# 21. Definition of Done for a work package

A work package is DONE only when:

- requested behavior exists;
- code compiles or all available compile checks pass;
- acceptance criteria are checked;
- obvious edge cases are handled;
- no unrelated feature was added;
- relevant tests were added where practical;
- TODOs/limitations are listed;
- this roadmap checkbox is updated only if the package is genuinely complete.

Do NOT automatically start the next package.

---

# 22. Work-package sizing

The user wants Codex sessions to use roughly half of a five-hour usage window rather than consuming the entire limit in one request.

Therefore:

- Take exactly ONE work package per Codex request unless explicitly told otherwise.
- Work packages below are intentionally scoped as medium-sized chunks.
- Do not combine adjacent packages just because there is remaining context.
- If a package turns out larger than expected, finish the smallest coherent vertical slice and report the remainder.
- Correctness and a clean stopping point are more important than maximizing the amount of code produced.

No duration is guaranteed; actual effort depends on the existing repository.

---

# 23. Demo roadmap

Statuses:

- `[ ]` not started
- `[~]` in progress / partial
- `[x]` done

Never mark `[x]` unless the Definition of Done is met.

---

## WP-00 — Repository audit and prototype foundation

Status: [x]

Goal:
Understand the existing Unity project before adding gameplay systems.

Tasks:

- Inspect repository structure.
- Identify Unity version and render pipeline.
- Identify existing scenes, packages, input system and code conventions.
- Confirm `.gitignore` is suitable for Unity.
- Create only the minimal folders needed for the tactical prototype.
- Add a lightweight bootstrap entry point only if the project lacks one.
- Document discovered constraints in a short `Docs/TECHNICAL_NOTES.md`.
- Do not build gameplay yet unless needed to verify setup.

Acceptance criteria:

- Project structure is understood.
- No Unity-generated cache files are accidentally tracked.
- A clear place exists for new tactical code.
- Technical notes describe how to open/run the prototype.

Codex completion report must include:
- Unity version;
- current scene(s);
- input approach;
- test infrastructure;
- important existing architecture that must be preserved.

---

## WP-01 — Hex coordinates and 12x12 grid model

Status: [x]

Goal:
Create the authoritative hex-grid data model.

Tasks:

- Implement axial hex coordinates.
- Implement six neighbor directions.
- Implement hex distance.
- Create a 12x12 logical grid.
- Provide cell lookup.
- Add terrain state placeholders.
- Add occupancy support.
- Add unit tests for coordinate math if test infrastructure is available.

Do not implement movement/pathfinding yet.

Acceptance criteria:

- Every valid cell can be addressed deterministically.
- Neighbor calculations work at edges.
- Distance is correct for representative cases.
- Invalid coordinates are handled safely.
- Grid dimensions/config are not scattered as magic numbers.

---

## WP-02 — Hex grid rendering and mouse selection

Status: [x]

Goal:
Make the logical grid visible and selectable in Unity.

Tasks:

- Render 12x12 hex tiles using placeholder visuals.
- Convert axial coordinates <-> Unity world positions.
- Add hover highlighting.
- Add selected-cell highlighting.
- Add mouse click selection.
- Display/debug selected coordinates.
- Keep rendering separate from the authoritative grid model.

Acceptance criteria:

- 12x12 grid appears in a prototype scene.
- Hover selects the correct hex.
- Clicking a cell selects exactly that cell.
- Camera framing shows the battlefield clearly.
- Coordinate/world conversions are centralized.

---

## WP-03 — Pathfinding and movement range

Status: [x]

Goal:
Calculate legal movement on the hex grid.

Tasks:

- Implement BFS/Dijkstra/A* as appropriate.
- Respect blocked cells.
- Respect occupied cells.
- Support movement costs in a future-friendly way.
- Calculate reachable cells for a movement budget.
- Show movement-range highlights.
- Add algorithm tests when practical.

Acceptance criteria:

- Paths never cross blocked cells.
- Reachable-cell calculation respects movement points.
- Paths are deterministic for the same state.
- Edge cells work correctly.
- Algorithm is not coupled to sprites/animations.

---

## WP-04 — Unit runtime model and player movement

Status: [x]

Goal:
Place heroes on the grid and move one selected hero.

Tasks:

- Create static unit definitions and separate runtime state.
- Spawn four placeholder heroes.
- Implement unit selection.
- Show selected unit movement range.
- Click destination to move.
- Update occupancy correctly.
- Add simple movement animation/interpolation.
- Prevent illegal movement.

Acceptance criteria:

- Four heroes spawn on valid hexes.
- One hero can be selected and moved.
- Occupied cells are unavailable.
- Logical position updates exactly once.
- Visual position ends aligned with the logical cell.

---

## WP-05 — Turn manager and action state machine

Status: [~]

Goal:
Create a robust turn loop.

Tasks:

- Add initiative values.
- Build turn order.
- Add states such as:
  - AwaitTurn
  - SelectingAction
  - Moving
  - ResolvingAction
  - TurnComplete
- Start/end turns.
- Reset movement/action availability at turn start.
- Add temporary turn-order UI.
- Prevent inputs during action resolution.

Acceptance criteria:

- Multiple units take turns in stable order.
- A unit cannot act outside its turn.
- A turn can always be completed.
- Player input cannot double-trigger during movement/action animations.
- Turn order is visible.

---

## WP-06 — Dice service and basic attacks

Status: [~]

Goal:
Make the first real combat interaction.

Tasks:

- Implement an injectable/testable random/dice service.
- Implement d20 attack roll.
- Resolve:
  `d20 + Attack >= Defense`
- Implement weapon damage die + bonus.
- Apply HP damage.
- Handle death state.
- Add melee attack targeting/range.
- Show compact floating result:
  - roll/hit/miss;
  - damage.
- Add deterministic seed/test mode if practical.

Acceptance criteria:

- A hero can attack an enemy in range.
- Hit/miss follows the documented formula.
- Damage cannot resolve twice from one action.
- Unit death is handled cleanly.
- Dice logic can be tested independently from visuals.

---

## WP-07 — Enemy units and simple melee AI

Status: [~]

Goal:
Create a playable player-vs-AI combat loop.

Tasks:

- Spawn several enemy units.
- Add enemy turns to initiative.
- Implement simple melee AI:
  - attack if possible;
  - otherwise path toward nearest valid visible hero;
  - move;
  - attack if now possible;
  - end turn.
- Handle unreachable targets safely.
- Add battle victory/defeat detection.

Acceptance criteria:

- Player and enemies alternate turns.
- Enemy does not freeze the turn loop.
- Enemy can navigate around obstacles.
- Combat ends with a clear victory/defeat state.
- No endless loop if no target is reachable.

Milestone:
At the end of WP-07, the project must contain the first genuinely playable grey-box battle.

---

## WP-08 — Four classes and eight class abilities

Status: [~]

Goal:
Give the four heroes distinct tactical identities.

Implement exactly two signature abilities per class for this package.

Suggested initial set:

Warrior:
- Heavy Strike
- Push

Rogue:
- Backstab
- Evade/Smoke

Ranger:
- Aimed Shot
- Trap

Mage:
- Fire Burst
- Blink

Tasks:

- Create a data-driven ability definition format.
- Create ability targeting rules.
- Create an execution pipeline.
- Implement the eight abilities.
- Add temporary action-bar UI.
- Add cooldown/resource only if an ability strictly requires it; otherwise avoid it.

Acceptance criteria:

- Each class plays differently.
- Ability behavior is data-driven where sensible.
- Target validation is centralized.
- Ability resolution cannot occur twice from one click.
- No general scripting language/editor is introduced yet.

---

## WP-09 — Terrain: blocked, high ground and pit

Status: [~]

Goal:
Add small positional terrain rules.

Tasks:

- Add visible terrain categories:
  - Ground
  - Blocked
  - HighGround
  - Pit
- Make pathfinding respect them.
- Implement Push interaction with Pit.
- Decide and implement one simple HighGround rule OR leave bonus disabled but represent the terrain.
- Update highlights and targeting accordingly.

Acceptance criteria:

- Units cannot walk through Blocked/Pit.
- Push can move a valid target one hex.
- A pushed unit entering Pit receives the defined fatal/severe result.
- Terrain logic lives in gameplay code, not only visuals.

---

## WP-10 — Fog of war and vision

Status: [ ]

Goal:
Make exploration uncertain.

Tasks:

- Implement Unknown/Explored/Visible.
- Compute party visibility from hero vision ranges.
- Hide unseen enemies.
- Preserve explored terrain.
- Refresh vision after movement/death/spawn.
- Add a debug toggle to visualize visibility state.

Acceptance criteria:

- New areas begin hidden.
- Moving reveals cells.
- Leaving an area makes it Explored rather than fully Unknown.
- Enemies disappear when no longer Visible.
- Vision refresh does not break turn input.

---

## WP-11 — Seeded procedural tactical map v1

Status: [ ]

Goal:
Generate a valid playable 12x12 dungeon.

Tasks:

- Add generator config.
- Add numeric/string seed support.
- Generate:
  - player spawn region;
  - connected traversable regions;
  - blocked cells;
  - optional high ground;
  - optional pits;
  - enemy spawn candidates;
  - objective/extraction candidate.
- Validate connectivity.
- Retry/fallback safely if generation fails.
- Display/log the seed.

Acceptance criteria:

- Same seed produces the same layout.
- Player start and objective are connected.
- Generated maps do not trap all heroes at spawn.
- Invalid generation fails gracefully.
- At least 20 seeds can be generated in a test/debug run without fatal errors.

---

## WP-12 — Procedural encounter placement

Status: [ ]

Goal:
Populate generated maps with varied but valid encounters.

Tasks:

- Create simple encounter budget/config.
- Add 3-5 enemy archetype definitions.
- Place enemies on legal cells away from immediate player spawn.
- Ensure enemies do not overlap.
- Add one mini-boss encounter option.
- Keep encounter generation seed-driven.

Acceptance criteria:

- Generated battle starts with a valid party/enemy placement.
- Same seed/config is reproducible.
- Enemy count stays within configured limits.
- No enemy spawns in blocked/pit cells.
- Mini-boss does not appear in every run.

---

## WP-13 — Chest, simple loot and extraction

Status: [ ]

Goal:
Create a complete expedition inside the tactical scene.

Tasks:

- Add one interactable chest/reward source.
- Add gold.
- Add minimal item definitions:
  - weapon;
  - armor;
  - healing consumable.
- Add tactical objective/extraction cell.
- Add expedition result data.
- Return victory loot/results to a temporary result screen.

Acceptance criteria:

- A run can begin, be completed and produce loot.
- Loot is deterministic with seed where intended.
- Items exist as data, not hardcoded UI strings only.
- Extraction/result flow works after combat.

---

## WP-14 — Guild state and roster screen

Status: [ ]

Goal:
Create the first meta-loop outside combat.

Tasks:

- Implement persistent-in-session GuildState.
- Add gold.
- Add roster of adventurers.
- Add four-person party selection.
- Add simple guild UI screen.
- Launch expedition with selected party.
- Return surviving party and rewards to guild.

Acceptance criteria:

- Guild -> expedition -> guild loop works.
- Selected adventurers are the ones spawned.
- HP/death/rewards return correctly.
- Starting a second expedition works without restarting the application.

---

## WP-15 — Death, corpse recovery and resurrection v1

Status: [ ]

Goal:
Make character loss meaningful.

Tasks:

- Represent dead adventurer state.
- Represent recoverable body.
- Decide a simple recovery rule for v0.1.
- Return recovered corpses to guild.
- Add resurrection cost.
- Permanently mark/loss-handle abandoned characters.
- Add clear UI confirmation before irreversible abandonment if appropriate.

Acceptance criteria:

- Death persists after expedition.
- Recovered character can be resurrected if requirements are met.
- Abandoned character cannot silently reappear.
- Gold cost is applied exactly once.

---

## WP-16 — Expedition selection and repeatable dungeon loop

Status: [ ]

Goal:
Turn the prototype into the basic demo game loop.

Tasks:

- Add expedition selection screen/panel.
- Show:
  - biome/type;
  - approximate difficulty;
  - possible reward;
  - seed if debug mode.
- Launch generated crypt/cellar expedition.
- Return results to guild.
- Allow immediate next expedition.

Acceptance criteria:

- Player can complete multiple runs in one session.
- Each expedition can produce a different generated map/encounter.
- Guild resources/roster evolve between runs.
- No world map is required yet.

Milestone:
At the end of WP-16, the core Demo v0.1 loop exists.

---

## WP-17 — Demo UX pass

Status: [ ]

Goal:
Make the game understandable without developer knowledge.

Tasks:

- Improve selection/movement/target highlights.
- Add concise tooltips for stats/abilities.
- Add turn indicator.
- Add combat feedback.
- Add invalid-action feedback.
- Add pause/options stub if needed.
- Add restart/return-to-guild flow.
- Remove developer-only controls from normal UI while preserving debug mode.

Acceptance criteria:

- A new tester can understand whose turn it is.
- A new tester can discover how to move and attack.
- Invalid actions are explained or visually obvious.
- Core combat does not require reading console logs.

---

## WP-18 — Pixel-art presentation pass

Status: [ ]

Goal:
Replace the grey-box feel with a coherent dark visual prototype.

Tasks:

- Establish pixel-perfect rendering approach appropriate to the project.
- Add temporary/final-enough terrain tiles.
- Add hero/enemy placeholder sprites with readable silhouettes.
- Add basic idle/move/attack/hit/death animations where feasible.
- Add restrained effects for dice/hits.
- Add dark crypt background/lighting treatment consistent with pixel art.

Acceptance criteria:

- Grid remains readable.
- Units are visually distinguishable.
- Pixel art is not blurred by camera/import settings.
- Gameplay information remains clearer than decoration.

---

## WP-19 — Save/load for demo progression

Status: [ ]

Goal:
Persist the guild between game launches.

Tasks:

- Define save schema/version.
- Save:
  - guild gold;
  - roster;
  - character state;
  - inventory;
  - relevant progression.
- Load safely.
- Handle missing/corrupted save with a safe fallback.
- Do not serialize Unity scene objects directly.

Acceptance criteria:

- Close/reopen preserves demo progress.
- New-game path works.
- Old/missing save does not hard-crash.
- Save model is versioned.

---

## WP-20 — Steam demo readiness / stabilization

Status: [ ]

Goal:
Prepare a stable external demo build.

Tasks:

- Audit errors/warnings.
- Profile obvious hot spots.
- Test repeated expedition loops.
- Test representative seeds.
- Remove debug-only cheats from release UI.
- Add build configuration/documentation.
- Verify input, resolution and common desktop behavior.
- Add crash-safe logging where reasonable.
- Create a final demo QA checklist.

Do NOT add Steam Workshop or multiplayer in this package.

Acceptance criteria:

- Build can be produced repeatably.
- Core loop works from fresh start through several expeditions.
- No known blocker prevents an external tester from playing.
- Remaining issues are documented and prioritized.

---

# 24. Explicitly out of Demo v0.1 scope

Do NOT implement unless the user explicitly changes scope:

- multiplayer/networking;
- Steam Workshop;
- full campaign/dialogue/NPC editor;
- AI-generated campaigns;
- world map and road encounters;
- multiple story campaigns;
- 20 levels / large class roster / hundreds of abilities;
- D&D compatibility;
- multi-floor maps;
- general environmental simulation;
- crafting/base-building/complex economy;
- cinematic dialogue/voice acting.

Future direction after the demo is proven:
world map -> travel encounters -> more biomes -> deeper guild -> story framework -> map editor -> campaign tools -> Workshop -> multiplayer.

Do not architect the current prototype around speculative future features.

# 25. Product identity

> A dark, compact, replayable hex-tactics RPG where the player manages a guild of vulnerable adventurers and relies on visible dice, procedural expeditions and risky decisions.

Protect these differentiators:

- hex tactics;
- visible dice/randomness;
- dangerous adventurers;
- procedural tactical runs;
- guild meta-progression;
- eventual creator/mod support.

When scope conflicts with ambition, choose the version a solo developer can finish and test.

# 26. How to ask Codex to work

Recommended prompt:

```text
Read AGENTS.md and inspect the repository first.

Work only on WP-XX.
Do not start the next work package.

Implement it to its acceptance criteria.
Reuse existing architecture where reasonable.
Run all available compile/tests/checks.

At the end:
1. summarize changes;
2. list changed files;
3. list tests/checks;
4. mention limitations/TODOs;
5. update only WP-XX status if truly complete.
```

For partial work:

```text
Read AGENTS.md and inspect WP-XX.
Continue only WP-XX from its current state.
Finish unmet acceptance criteria.
Do not begin the next package.
```

For bugs:

```text
Read AGENTS.md.
Investigate root cause before editing.
Make the smallest safe fix and add a regression test if practical.
Do not expand scope.

Bug:
<describe bug>
```

# 27. Immediate starting order

Start with:

`WP-00 -> WP-01 -> WP-02 -> WP-03 -> WP-04 -> WP-05 -> WP-06 -> WP-07`

Do not skip directly to procedural generation.

The first major checkpoint is WP-07:

> Four heroes and enemies can move, take turns, roll dice, attack, die, and finish a complete grey-box battle.

Only after that checkpoint should deeper content systems be built.

<!-- End -->
