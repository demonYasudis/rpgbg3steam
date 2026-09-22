using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Abilities;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class AbilityChecks
    {
        private sealed class MaximumDice : IDice { public int Roll(int sides) => sides; }
        private sealed class ConstantDice : IDice
        {
            private readonly int value;
            public ConstantDice(int value) { this.value = value; }
            public int Roll(int sides) => Math.Min(value, sides);
        }
        private sealed class BrokenAreaDice : IDice
        {
            private int count;
            public int Roll(int sides) => ++count == 3 ? 0 : sides;
        }

        private sealed class Fixture
        {
            public readonly GridModel Grid = new GridModel(9, 7);
            public readonly UnitRuntimeState Actor, Enemy;
            public readonly TurnManager Turns;
            public readonly List<UnitRuntimeState> Units = new List<UnitRuntimeState>();
            public readonly IDice Dice;
            public Fixture(int hero, HexCoordinates? enemy = null, IDice dice = null)
            {
                Dice = dice ?? new MaximumDice();
                Actor = Add("hero", HeroDefinitions.Defaults[hero], new HexCoordinates(1, 1), UnitTeam.Player);
                Enemy = Add("enemy", new UnitDefinition("enemy", "Enemy", 4, maxHealth: 40),
                    enemy ?? new HexCoordinates(2, 1), UnitTeam.Enemy);
                Turns = new TurnManager(Grid, Units);
                Require(Turns.TryStartNextTurn() && Turns.ActiveUnit == Actor, "Hero starts fixture");
            }
            public UnitRuntimeState Add(string id, UnitDefinition definition, HexCoordinates position, UnitTeam team)
            {
                Require(UnitRuntimeState.TrySpawn(Grid, id, definition, position, out var unit, team), "Spawn " + id);
                Units.Add(unit);
                return unit;
            }
            public AbilitySystem System() => new AbilitySystem(Grid, Turns, Dice, Units);
            public AbilityDefinition Ability(int index) => Actor.Definition.Abilities[index];
            public void NextActor()
            {
                Turns.TryCompleteAction(Turns.ActiveUnit);
                Require(Turns.TryEndTurn(Turns.ActiveUnit), "End current turn");
                Require(Turns.TryStartNextTurn(), "Start next turn");
                while (Turns.ActiveUnit != Actor)
                {
                    Require(Turns.TryEndTurn(Turns.ActiveUnit) && Turns.TryStartNextTurn(), "Pass other turns");
                }
            }
        }

        [MenuItem("Tools/Guild Tactics/Validate Class Abilities")]
        public static void Run()
        {
            Require(HeroDefinitions.Defaults.All(hero => hero.Abilities.Count == 2), "Exactly two signatures per class");
            Require(HeroDefinitions.Defaults.SelectMany(hero => hero.Abilities).Select(a => a.Effect).Distinct().Count() == 8,
                "Eight distinct effects");
            CheckHeavyStrikeAndValidation();
            CheckBackstab();
            CheckPush();
            CheckEvade();
            CheckRangedAttacks();
            CheckTraps();
            CheckFireBurst();
            CheckBlink();
            Debug.Log("WP-08 model checks passed: all eight abilities, range/ownership, action locks, buffs, traps, area atomicity and relocation.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        private static PlayerUnitController controller;
        private static UnitRuntimeState presentationActor;
        private static int presentationStage;

        internal static void BeginPresentation(PlayerUnitController scene)
        {
            Require(scene.GetComponent<ActionBarUI>() != null, "Saved scene wires action bar");
            var grid = new GridModel();
            var layout = new HexLayout();
            var root = new GameObject("WP08 Ability Checks");
            var view = root.AddComponent<HexGridView>();
            view.Initialize(grid, layout);
            var input = root.AddComponent<HexGridInteraction>();
            input.Initialize(grid, layout, view, scene.GetComponent<HexGridInteraction>().GridCamera);
            controller = root.AddComponent<PlayerUnitController>();
            controller.Initialize(grid, layout, view, input, 0, new MaximumDice(), enableBattle: false);
            root.AddComponent<ActionBarUI>().Initialize(controller);
            presentationActor = controller.SelectedUnit;
            Require(controller.SelectAbility(presentationActor.Definition.Abilities[1]), "Select Evade");
            Require(!controller.TryUseSelectedAbility(controller.Enemies[0].Position) && controller.Turns.ActionAvailable,
                "Invalid target keeps action available");
            // Use the same pointer dispatch as runtime, including self-targeting.
            input.ProcessPointer(input.GridCamera.WorldToScreenPoint(layout.ToWorld(presentationActor.Position)), true);
            Require(controller.LastAbility != null && presentationActor.EvasionBonus == 4 &&
                controller.Turns.State == TurnState.ResolvingAction && controller.SelectedAbility == null &&
                !controller.SelectBasicAttack() && !controller.TryUseSelectedAbility(presentationActor.Position) &&
                !controller.TryEndTurn(), "Pointer commits ability once and locks commands");
            presentationStage = 0;
        }

        internal static bool PollPresentation()
        {
            if (presentationStage == 0)
            {
                if (controller.Turns.State == TurnState.ResolvingAction) return false;
                Require(!controller.Turns.ActionAvailable && presentationActor.EvasionBonus == 4 &&
                    controller.TryEndTurn(), "Feedback completes without restoring action");
                presentationStage = 1;
                return false;
            }
            if (controller.Turns.State == TurnState.TurnComplete) return false;
            Require(controller.SelectedUnit.Definition.Id == "ranger" && controller.SelectedAbility == null,
                "Next hero starts without stale targeting");
            Require(controller.SelectAbility(controller.SelectedUnit.Definition.Abilities[0]) &&
                controller.TryUseSelectedAbility(controller.Enemies[0].Position), "Ranger aimed shot through controller");
            int health = controller.Enemies[0].CurrentHealth;
            controller.enabled = false;
            controller.enabled = true;
            Require(controller.Turns.State == TurnState.SelectingAction && !controller.Turns.ActionAvailable &&
                !controller.SelectAbility(controller.SelectedUnit.Definition.Abilities[0]) &&
                controller.Enemies[0].CurrentHealth == health && controller.TryEndTurn(),
                "Interrupted ability preserves damage and spent action without stranding turn");
            UnityEngine.Object.Destroy(controller.gameObject);
            Debug.Log("WP-08 Play Mode checks passed: action bar wiring, pointer targeting, invalid targets, resolution locks and disable recovery.");
            return true;
        }

        private static void CheckHeavyStrikeAndValidation()
        {
            var f = new Fixture(0);
            var system = f.System();
            Require(!system.TryUse(null, f.Ability(0), f.Enemy.Position, out _, out _) &&
                !system.TryUse(f.Actor, null, f.Enemy.Position, out _, out _) &&
                !system.TryUse(f.Enemy, f.Ability(0), f.Actor.Position, out _, out _) &&
                !system.TryUse(f.Actor, HeroDefinitions.Defaults[1].Abilities[1], f.Actor.Position, out _, out _) &&
                !system.TryUse(f.Actor, new AbilityDefinition("heavy-strike", "Forged", "", AbilityEffect.HeavyStrike, 9, 99),
                    f.Enemy.Position, out _, out _) &&
                !system.TryUse(f.Actor, f.Ability(0), new HexCoordinates(-1, 1), out _, out _) &&
                !system.TryUse(f.Actor, f.Ability(0), f.Actor.Position, out _, out _) && f.Turns.ActionAvailable,
                "Invalid actor, ownership, forged definition, range and ally spend nothing");
            Require(system.TryUse(f.Actor, f.Ability(0), f.Enemy.Position, out var result, out _) &&
                result.Attacks[0].Damage == 12 && result.Attacks[0].AttackBonus == 2 && f.Enemy.CurrentHealth == 28,
                "Heavy strike modifiers");
            Require(f.Turns.State == TurnState.ResolvingAction && !f.Turns.ActionAvailable &&
                !system.TryUse(f.Actor, f.Ability(0), f.Enemy.Position, out _, out _) &&
                !f.Turns.TryEndTurn(f.Actor) && !f.Turns.TryBeginMovement(f.Actor, new HexCoordinates(0, 1), out _),
                "Single commit and immediate locks");
            f.Turns.TryCompleteAction(f.Actor);
            Require(!system.TryUse(f.Actor, f.Ability(0), f.Enemy.Position, out _, out _) && f.Enemy.CurrentHealth == 28,
                "Completion cannot restore action or repeat damage");
            f.NextActor();
            Require(system.CanUse(f.Actor, f.Ability(0), f.Enemy.Position, out _), "No hidden cooldown");
            var miss = new Fixture(0, dice: new ConstantDice(9));
            Require(miss.System().TryUse(miss.Actor, miss.Ability(0), miss.Enemy.Position, out result, out _) &&
                !result.Attacks[0].Hit && miss.Enemy.CurrentHealth == 40, "Heavy accuracy penalty can miss");
        }

        private static void CheckBackstab()
        {
            var f = new Fixture(1);
            Require(!f.System().CanUse(f.Actor, f.Ability(0), f.Enemy.Position, out _) && f.Turns.ActionAvailable,
                "Backstab requires ally support");
            var ally = f.Add("ally", HeroDefinitions.Defaults[0], new HexCoordinates(2, 0), UnitTeam.Player);
            var system = f.System();
            Require(system.TryUse(f.Actor, f.Ability(0), f.Enemy.Position, out var result, out _) &&
                result.Attacks[0].Damage == 13 && ally.CurrentHealth == 20, "Supported backstab deals bonus damage");
            f.NextActor();
            ally.ApplyDamage(ally.CurrentHealth);
            Require(!system.CanUse(f.Actor, f.Ability(0), f.Enemy.Position, out _), "Dead ally cannot enable backstab");
        }

        private static void CheckPush()
        {
            var f = new Fixture(0);
            var system = f.System();
            var landing = new HexCoordinates(3, 1);
            foreach (var terrain in new[] { TerrainType.Blocked })
            {
                f.Grid.GetCell(landing).Terrain = terrain;
                Require(!system.CanUse(f.Actor, f.Ability(1), f.Enemy.Position, out _), "Push rejects blocked landing");
            }
            f.Grid.GetCell(landing).Terrain = TerrainType.Ground;
            f.Grid.TryOccupy(landing, "blocker");
            Require(!system.CanUse(f.Actor, f.Ability(1), f.Enemy.Position, out _), "Occupied landing rejected");
            f.Grid.TryVacate(landing, "blocker");
            f.Turns.Traps.Place(f.Actor, landing, 8);
            var old = f.Enemy.Position;
            Require(system.TryUse(f.Actor, f.Ability(1), old, out var result, out _) && f.Enemy.Position == landing &&
                !f.Grid.GetCell(old).IsOccupied && f.Grid.GetCell(landing).OccupantId == f.Enemy.InstanceId &&
                f.Enemy.CurrentHealth == 32 && result.TrapHit.Damage == 8 && f.Turns.RemainingMovement == 4,
                "Push relocates once, triggers hostile landing trap and preserves actor movement");
            var edge = new Fixture(0, new HexCoordinates(0, 1));
            Require(!edge.System().CanUse(edge.Actor, edge.Ability(1), edge.Enemy.Position, out _), "No off-map push");
        }

        private static void CheckEvade()
        {
            var f = new Fixture(1);
            var system = f.System();
            Require(!system.CanUse(f.Actor, f.Ability(1), f.Enemy.Position, out _), "Evade self only");
            Require(system.TryUse(f.Actor, f.Ability(1), f.Actor.Position, out _, out _) &&
                f.Actor.Defense == 16 && f.Actor.Definition.Defense == 12, "Runtime buff does not mutate shared definition");
            f.Turns.TryCompleteAction(f.Actor);
            f.Turns.TryEndTurn(f.Actor);
            f.Turns.TryStartNextTurn();
            var combat = new CombatSystem(f.Grid, f.Turns, new ConstantDice(8));
            Require(combat.TryAttack(f.Enemy, f.Actor, out var result) && !result.Hit && f.Actor.CurrentHealth == 20,
                "Defense buff applies throughout enemy turn");
            f.NextActor();
            Require(f.Actor.Defense == 12 && f.Actor.EvasionBonus == 0, "Buff expires at own next turn");
        }

        private static void CheckRangedAttacks()
        {
            var f = new Fixture(2, new HexCoordinates(6, 1));
            var combat = new CombatSystem(f.Grid, f.Turns, f.Dice);
            Require(!combat.CanAttack(f.Actor, f.Enemy), "Basic ranged attack respects range four");
            Require(f.System().TryUse(f.Actor, f.Ability(0), f.Enemy.Position, out var result, out _) &&
                result.Attacks[0].AttackBonus == 7 && result.Attacks[0].Damage == 10, "Aimed shot range and modifiers");
            var boundary = new Fixture(2, new HexCoordinates(5, 1));
            Require(new CombatSystem(boundary.Grid, boundary.Turns, boundary.Dice).TryAttack(boundary.Actor, boundary.Enemy, out _),
                "Ranger basic attack reaches four");
            var mage = new Fixture(3, new HexCoordinates(4, 1));
            Require(new CombatSystem(mage.Grid, mage.Turns, mage.Dice).TryAttack(mage.Actor, mage.Enemy, out _),
                "Mage basic magic reaches three");
            var far = new Fixture(2, new HexCoordinates(8, 1));
            Require(!far.System().CanUse(far.Actor, far.Ability(0), far.Enemy.Position, out _), "Aimed shot bounded at six");
        }

        private static void CheckTraps()
        {
            var f = new Fixture(2, new HexCoordinates(4, 1));
            var system = f.System();
            var trap = new HexCoordinates(3, 1);
            Require(!system.CanUse(f.Actor, f.Ability(1), f.Enemy.Position, out _), "Trap rejects occupied cell");
            Require(system.TryUse(f.Actor, f.Ability(1), trap, out _, out _) && f.Turns.Traps.Contains(trap), "Arm trap");
            f.NextActor();
            Require(!system.CanUse(f.Actor, f.Ability(1), trap, out _), "Cannot stack traps");
            Require(system.TryUse(f.Actor, f.Ability(1), new HexCoordinates(2, 1), out _, out _) &&
                !f.Turns.Traps.Contains(trap) && f.Turns.Traps.Traps.Count == 1, "New trap replaces owner's previous trap");
            f.Turns.TryCompleteAction(f.Actor);
            Require(f.Turns.TryBeginMovement(f.Actor, new HexCoordinates(2, 1), out _) && f.Actor.CurrentHealth == 20 &&
                f.Turns.Traps.Traps.Count == 1, "Ally crossing is safe and preserves trap");
            f.Turns.TryCompleteMovement(f.Actor);
            Require(f.Turns.TryBeginMovement(f.Actor, new HexCoordinates(1, 1), out _), "Ally leaves trap");
            f.Turns.TryCompleteMovement(f.Actor);
            f.Turns.TryEndTurn(f.Actor);
            f.Turns.TryStartNextTurn();
            Require(f.Turns.TryBeginMovement(f.Enemy, new HexCoordinates(2, 1), out _) && f.Enemy.CurrentHealth == 32 &&
                f.Turns.Traps.Traps.Count == 0 && f.Turns.LastTrapHits.Count == 1, "Enemy entry consumes trap once");
            f.Turns.TryCompleteMovement(f.Enemy);
            Require(f.Turns.TryBeginMovement(f.Enemy, new HexCoordinates(3, 1), out _) && f.Enemy.CurrentHealth == 32,
                "Moving again does not repeat trap damage");

            var lethal = new Fixture(2, new HexCoordinates(4, 1));
            var lethalSystem = lethal.System();
            lethal.Enemy.ApplyDamage(35);
            Require(lethalSystem.TryUse(lethal.Actor, lethal.Ability(1), trap, out _, out _), "Lethal trap prepared");
            lethal.Turns.TryCompleteAction(lethal.Actor);
            lethal.Turns.TryEndTurn(lethal.Actor);
            lethal.Turns.TryStartNextTurn();
            Require(lethal.Turns.TryBeginMovement(lethal.Enemy, new HexCoordinates(2, 1), out var path) && path.Count == 2 &&
                lethal.Enemy.Position == trap && !lethal.Enemy.IsAlive && !lethal.Grid.GetCell(trap).IsOccupied &&
                lethal.Turns.LastTrapHits[0].Killed, "Lethal crossing truncates path and frees occupancy at trap");
            Require(lethal.Turns.TryCompleteMovement(lethal.Enemy) && lethal.Turns.TryEndTurn(lethal.Enemy) &&
                lethal.Turns.TryStartNextTurn() && lethal.Turns.ActiveUnit == lethal.Actor, "Death in movement never strands turn");
        }

        private static void CheckFireBurst()
        {
            var f = new Fixture(3, new HexCoordinates(3, 1));
            var second = f.Add("second", new UnitDefinition("second", "Second", 2), new HexCoordinates(3, 2), UnitTeam.Enemy);
            var ally = f.Add("ally", HeroDefinitions.Defaults[0], new HexCoordinates(2, 2), UnitTeam.Player);
            var system = f.System();
            second.ApplyDamage(15);
            Require(system.TryUse(f.Actor, f.Ability(0), f.Enemy.Position, out var result, out _) && result.Attacks.Count == 2 &&
                f.Enemy.CurrentHealth == 32 && !second.IsAlive && ally.CurrentHealth == 20 && f.Actor.CurrentHealth == 20 &&
                !f.Grid.GetCell(second.Position).IsOccupied, "Burst rolls per enemy, kills cleanly and excludes allies");
            var broken = new Fixture(3, new HexCoordinates(3, 1), new BrokenAreaDice());
            var other = broken.Add("other", new UnitDefinition("other", "Other", 2), new HexCoordinates(3, 2), UnitTeam.Enemy);
            bool threw = false;
            try { broken.System().TryUse(broken.Actor, broken.Ability(0), broken.Enemy.Position, out _, out _); }
            catch (InvalidOperationException) { threw = true; }
            Require(threw && broken.Enemy.CurrentHealth == 40 && other.CurrentHealth == 20 &&
                broken.Turns.State == TurnState.SelectingAction && !broken.Turns.ActionAvailable,
                "Bad second roll causes no partial area damage and releases lock");
        }

        private static void CheckBlink()
        {
            var f = new Fixture(3);
            var system = f.System();
            var target = new HexCoordinates(4, 1);
            for (int r = 0; r < f.Grid.Height; r++) f.Grid.GetCell(new HexCoordinates(3, r)).Terrain = TerrainType.Blocked;
            Require(!system.CanUse(f.Actor, f.Ability(1), f.Enemy.Position, out _) &&
                !system.CanUse(f.Actor, f.Ability(1), new HexCoordinates(3, 1), out _) &&
                !system.CanUse(f.Actor, f.Ability(1), new HexCoordinates(5, 1), out _), "Blink rejects occupancy, blocked landing and excess range");
            f.Grid.GetCell(target).Terrain = TerrainType.Pit;
            Require(!system.CanUse(f.Actor, f.Ability(1), target, out _), "Blink rejects pit");
            f.Grid.GetCell(target).Terrain = TerrainType.HighGround;
            f.Turns.Traps.Place(f.Enemy, target, 5);
            var start = f.Actor.Position;
            Require(system.TryUse(f.Actor, f.Ability(1), target, out var result, out _) && f.Actor.Position == target &&
                !f.Grid.GetCell(start).IsOccupied && f.Grid.GetCell(target).OccupantId == f.Actor.InstanceId &&
                f.Turns.RemainingMovement == 3 && f.Actor.CurrentHealth == 15 && result.TrapHit.Damage == 5,
                "Blink crosses wall, commits occupancy once, retains movement and triggers landing trap");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("WP-08 failed: " + message);
        }
    }
}
