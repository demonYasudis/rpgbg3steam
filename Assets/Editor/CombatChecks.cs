using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class CombatChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Basic Combat")]
        public static void Run()
        {
            CheckDice();
            CheckFormula();
            CheckTargetingAndLocks();
            CheckDeathAndTurns();
            CheckInvalidDice();
            Debug.Log("WP-06 model checks passed: seeded dice, formula boundaries, targeting, single resolution, HP/death, occupancy, dead turns and invalid providers.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        private static void CheckDice()
        {
            var first = new SeededDice(-12345);
            var second = new SeededDice(-12345);
            var different = new SeededDice(67890);
            bool differs = false;
            foreach (int sides in new[] { 1, 4, 6, 8, 20, int.MaxValue })
                for (int i = 0; i < 1000; i++)
                {
                    int roll = first.Roll(sides);
                    Require(roll >= 1 && roll <= sides && roll == second.Roll(sides), "Bounded reproducible dice");
                    differs |= roll != different.Roll(sides);
                }
            Require(differs && first.Seed == -12345, "Independent seeds and exposed seed");
            Throws<ArgumentOutOfRangeException>(() => first.Roll(0));
            Throws<ArgumentOutOfRangeException>(() => first.Roll(-1));
            Throws<ArgumentOutOfRangeException>(() => new UnitDefinition("bad", "Bad", 1, maxHealth: 0));
            Throws<ArgumentOutOfRangeException>(() => new UnitDefinition("bad", "Bad", 1, damageDie: 0));
        }

        private static void CheckFormula()
        {
            // The documented example: 13 + 4 >= 17; d8(5) + 2 = 7.
            var hit = new Fixture(4, 17, 8, 2, 20, 13, 5);
            Require(hit.Combat.TryAttack(hit.Hero, hit.Enemy, out var result), "Boundary hit accepted");
            Require(result.Hit && result.AttackTotal == 17 && result.Damage == 7 && result.AppliedDamage == 7 &&
                result.DamageRoll == 5 && hit.Enemy.CurrentHealth == 13 && !result.Killed, "Exact attack/damage formula");
            Require(hit.Dice.RequestedSides.SequenceEqual(new[] { 20, 8 }), "Correct dice requested");
            var miss = new Fixture(4, 17, 8, 2, 20, 12);
            Require(miss.Combat.TryAttack(miss.Hero, miss.Enemy, out result) && !result.Hit &&
                result.Damage == 0 && result.DamageRoll == 0 && miss.Enemy.CurrentHealth == 20 &&
                miss.Dice.RequestedSides.SequenceEqual(new[] { 20 }), "Miss consumes no damage roll and changes no HP");
            var lowRoll = new Fixture(100, 17, 1, 0, 20, 1, 1);
            Require(lowRoll.Combat.TryAttack(lowRoll.Hero, lowRoll.Enemy, out result) && result.Hit,
                "Natural one is not an automatic miss");
            var highRoll = new Fixture(-100, 17, 1, 0, 20, 20);
            Require(highRoll.Combat.TryAttack(highRoll.Hero, highRoll.Enemy, out result) && !result.Hit,
                "Natural twenty is not an automatic hit");
            var negative = new Fixture(4, 1, 6, int.MinValue, 20, 20, 1);
            Require(negative.Combat.TryAttack(negative.Hero, negative.Enemy, out result) && result.Hit &&
                result.Damage == 0 && negative.Enemy.CurrentHealth == 20, "Negative damage never heals");
            var extreme = new Fixture(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
                20, int.MaxValue);
            Require(extreme.Combat.TryAttack(extreme.Hero, extreme.Enemy, out result) && result.Hit &&
                result.AttackTotal == (long)int.MaxValue + 20 && result.Damage == int.MaxValue && result.Killed,
                "Attack and damage arithmetic cannot overflow");
        }

        private static void CheckTargetingAndLocks()
        {
            var f = new Fixture(4, 12, 6, 2, 20, 20, 6);
            var ally = Spawn(f.Grid, "ally", new HexCoordinates(1, 1), UnitTeam.Player);
            var far = Spawn(f.Grid, "far", new HexCoordinates(3, 3), UnitTeam.Enemy);
            var otherGrid = new GridModel(4, 4);
            var foreign = Spawn(otherGrid, "enemy", f.Enemy.Position, UnitTeam.Enemy);
            Require(!f.Combat.TryAttack(f.Hero, null, out _) && !f.Combat.TryAttack(null, f.Enemy, out _) &&
                !f.Combat.TryAttack(f.Hero, f.Hero, out _) && !f.Combat.TryAttack(f.Hero, ally, out _) &&
                !f.Combat.TryAttack(f.Hero, far, out _) && !f.Combat.TryAttack(ally, f.Enemy, out _) &&
                !f.Combat.TryAttack(f.Hero, foreign, out _), "Invalid targets and actors rejected");
            Require(f.Turns.ActionAvailable && f.Dice.RequestedSides.Count == 0, "Invalid input spends nothing");
            Require(f.Turns.TryBeginMovement(f.Hero, new HexCoordinates(1, 0), out _) &&
                !f.Combat.TryAttack(f.Hero, f.Enemy, out _) && f.Turns.ActionAvailable, "No attacking during movement");
            f.Turns.TryCompleteMovement(f.Hero);
            Require(f.Combat.TryAttack(f.Hero, f.Enemy, out _) && !f.Turns.ActionAvailable &&
                f.Turns.State == TurnState.ResolvingAction, "Attack locks input immediately");
            int hp = f.Enemy.CurrentHealth;
            for (int i = 0; i < 10; i++) Require(!f.Combat.TryAttack(f.Hero, f.Enemy, out _), "Repeated click rejected");
            Require(!f.Turns.TryEndTurn(f.Hero) && !f.Turns.TryBeginMovement(f.Hero, new HexCoordinates(2, 0), out _),
                "Resolving attack prevents move/end");
            Require(f.Turns.TryCompleteAction(f.Hero) && !f.Turns.TryCompleteAction(f.Hero) &&
                !f.Combat.TryAttack(f.Hero, f.Enemy, out _) && f.Enemy.CurrentHealth == hp &&
                f.Dice.RequestedSides.Count == 2, "Completion and repeat input cannot reapply damage");
            Require(f.Turns.TryEndTurn(f.Hero) && f.Turns.TryStartNextTurn() && f.Turns.ActionAvailable,
                "Next turn restores action");
            Throws<ArgumentException>(() => new CombatSystem(otherGrid, f.Turns, f.Dice));
            Throws<ArgumentException>(() => new TurnManager(otherGrid, new[] { f.Enemy }));
        }

        private static void CheckDeathAndTurns()
        {
            var f = new Fixture(4, 1, 8, 2, 3, 20, 8);
            var origin = f.Hero.Position;
            Require(f.Combat.TryAttack(f.Hero, f.Enemy, out var result) && result.Damage == 10 &&
                result.AppliedDamage == 3 && result.Killed && f.Enemy.CurrentHealth == 0 &&
                !f.Grid.GetCell(f.Enemy.Position).IsOccupied, "Lethal overkill clamps HP and releases cell");
            Require(!f.Enemy.TryMoveAlong(f.Grid, new[] { f.Enemy.Position, new HexCoordinates(2, 1) }),
                "Dead unit cannot move");
            f.Turns.TryCompleteAction(f.Hero);
            Require(f.Turns.TryBeginMovement(f.Hero, f.Enemy.Position, out _) &&
                f.Grid.GetCell(f.Enemy.Position).OccupantId == f.Hero.InstanceId && !f.Grid.GetCell(origin).IsOccupied,
                "Freed corpse cell is reachable");
            f.Turns.TryCompleteMovement(f.Hero);
            f.Turns.TryEndTurn(f.Hero);
            f.Turns.TryStartNextTurn();
            Require(!f.Combat.TryAttack(f.Hero, f.Enemy, out _) && f.Turns.ActionAvailable &&
                f.Grid.GetCell(f.Hero.Position).OccupantId == f.Hero.InstanceId, "Dead target cannot release a new occupant");
            var fresh = Spawn(f.Grid, "fresh", new HexCoordinates(3, 3), UnitTeam.Enemy, f.Enemy.Definition);
            Require(fresh.CurrentHealth == 3 && f.Enemy.Definition.MaxHealth == 3, "Shared definition health unchanged");

            var grid = new GridModel(4, 4);
            var enemy = Spawn(grid, "killer", new HexCoordinates(1, 1), UnitTeam.Enemy,
                new UnitDefinition("killer", "Killer", 1, 10, damageDie: 1, damageBonus: 20));
            var victim = Spawn(grid, "victim", new HexCoordinates(1, 2), UnitTeam.Player);
            var survivor = Spawn(grid, "survivor", new HexCoordinates(3, 3), UnitTeam.Player);
            var turns = new TurnManager(grid, new[] { enemy, victim, survivor });
            turns.TryStartNextTurn();
            var combat = new CombatSystem(grid, turns, new ScriptedDice(20, 1));
            Require(combat.TryAttack(enemy, victim, out _) && !victim.IsAlive, "Enemy can kill party member through same rules");
            turns.TryCompleteAction(enemy);
            turns.TryEndTurn(enemy);
            Require(turns.TryStartNextTurn() && turns.ActiveUnit == survivor && !turns.CanSelectAction(victim),
                "Dead initiative participant skipped");

            var onlyVictim = Spawn(grid, "only", new HexCoordinates(2, 1), UnitTeam.Player);
            var emptyTurns = new TurnManager(grid, new[] { onlyVictim });
            var killerTurns = new TurnManager(grid, new[] { enemy });
            killerTurns.TryStartNextTurn();
            Require(new CombatSystem(grid, killerTurns, new ScriptedDice(20, 1)).TryAttack(enemy, onlyVictim, out _),
                "Kill the last participant before its turn");
            Require(!emptyTurns.TryStartNextTurn() && emptyTurns.ActiveUnit == null && emptyTurns.Round == 0 &&
                !emptyTurns.ActionAvailable, "No living participant returns safely without endless loop");
        }

        private static void CheckInvalidDice()
        {
            foreach (int[] rolls in new[] { new[] { 0 }, new[] { 21 }, new[] { 20, 0 }, new[] { 20, 7 }, new[] { 20 } })
            {
                var f = new Fixture(4, 12, 6, 2, 20, rolls);
                Throws<InvalidOperationException>(() => f.Combat.TryAttack(f.Hero, f.Enemy, out _));
                Require(f.Enemy.CurrentHealth == 20 && f.Turns.State == TurnState.SelectingAction &&
                    !f.Turns.ActionAvailable && f.Turns.TryEndTurn(f.Hero), "Bad dice do not damage or softlock");
            }
        }

        private sealed class Fixture
        {
            public readonly GridModel Grid = new GridModel(4, 4);
            public readonly UnitRuntimeState Hero;
            public readonly UnitRuntimeState Enemy;
            public readonly TurnManager Turns;
            public readonly ScriptedDice Dice;
            public readonly CombatSystem Combat;

            public Fixture(int attack, int defense, int die, int bonus, int hp, params int[] rolls)
            {
                Hero = Spawn(Grid, "hero", new HexCoordinates(0, 0), UnitTeam.Player,
                    new UnitDefinition("hero", "Hero", 4, attack: attack, damageDie: die, damageBonus: bonus));
                Enemy = Spawn(Grid, "enemy", new HexCoordinates(0, 1), UnitTeam.Enemy,
                    new UnitDefinition("enemy", "Enemy", 1, maxHealth: hp, defense: defense));
                Turns = new TurnManager(Grid, new[] { Hero });
                Turns.TryStartNextTurn();
                Dice = new ScriptedDice(rolls);
                Combat = new CombatSystem(Grid, Turns, Dice);
            }
        }

        private sealed class ScriptedDice : IDice
        {
            private readonly Queue<int> rolls;
            public readonly List<int> RequestedSides = new List<int>();
            public ScriptedDice(params int[] values) => rolls = new Queue<int>(values);
            public int Roll(int sides)
            {
                RequestedSides.Add(sides);
                return rolls.Dequeue();
            }
        }

        private static GameObject testRoot;
        private static PlayerUnitController controller;
        private static HexGridInteraction interaction;
        private static CombatText feedback;
        private static GridModel playingGrid;
        private static UnitRuntimeState actor;
        private static UnitRuntimeState target;
        private static int attackCount;
        private static int expectedHealth;

        internal static void BeginPresentation(PlayerUnitController sceneController)
        {
            Require(sceneController.GetComponent<CombatText>() != null && sceneController.Enemies.Count == 3,
                "Saved scene wires feedback and three opponents");
            sceneController.enabled = false;
            playingGrid = new GridModel();
            var layout = new HexLayout();
            var camera = sceneController.GetComponent<HexGridInteraction>().GridCamera;
            testRoot = new GameObject("WP06 Combat Checks");
            var view = testRoot.AddComponent<HexGridView>();
            view.Initialize(playingGrid, layout);
            interaction = testRoot.AddComponent<HexGridInteraction>();
            interaction.Initialize(playingGrid, layout, view, camera);
            feedback = testRoot.AddComponent<CombatText>();
            feedback.Initialize(camera, SeededDice.DefaultSeed);
            controller = testRoot.AddComponent<PlayerUnitController>();
            controller.Initialize(playingGrid, layout, view, interaction, 0,
                new ScriptedDice(20, 6, 20, 6, 20, 6), feedback, enableBattle: false);
            actor = controller.SelectedUnit;
            target = controller.Enemies.Single();
            attackCount = 0;
            expectedHealth = target.CurrentHealth;
            Require(!controller.TrySelectUnit(target.Position) && controller.CanAttack(target), "Enemy is an attack target, not selectable hero");
            ClickAttack();
        }

        private static void ClickAttack()
        {
            var point = interaction.GridCamera.WorldToScreenPoint(new HexLayout().ToWorld(target.Position));
            interaction.ProcessPointer(point, true);
            attackCount++;
            expectedHealth = Math.Max(0, expectedHealth - 8);
            Require(target.CurrentHealth == expectedHealth && controller.LastAttack.Hit &&
                controller.Turns.State == TurnState.ResolvingAction && feedback.IsShowing &&
                ReferenceEquals(feedback.LastResult, controller.LastAttack), "Mouse attack commits once and displays result");
            interaction.ProcessPointer(point, true);
            Require(target.CurrentHealth == expectedHealth && !controller.TryAttackSelected(target.Position) &&
                !controller.TryMoveSelected(new HexCoordinates(0, 2)) && !controller.TryEndTurn(),
                "Repeated clicks and commands locked during result animation");
        }

        internal static bool PollPresentation()
        {
            Require(target.CurrentHealth == expectedHealth, "Waiting never reapplies damage");
            if (controller.Turns.State == TurnState.ResolvingAction) return false;
            if (controller.Turns.State == TurnState.TurnComplete) return false;
            if (controller.SelectedUnit != actor)
            {
                Require(controller.TryEndTurn(), "Other heroes can pass");
                return false;
            }
            if (!controller.Turns.ActionAvailable)
            {
                Require(!controller.TryAttackSelected(target.Position), "Cannot repeat attack after animation");
                if (attackCount == 3)
                {
                    Require(!target.IsAlive && !playingGrid.GetCell(target.Position).IsOccupied &&
                        controller.LastAttack.Killed && !controller.CanAttack(target), "Death frees target cell");
                    var deadView = controller.GetComponentsInChildren<UnitView>(true).Single(item => item.State == target);
                    Require(!deadView.gameObject.activeSelf, "Dead enemy view disabled");
                    Require(controller.TryMoveSelected(target.Position) && actor.Position == target.Position,
                        "Hero can enter released cell after death");
                    Require(controller.TryEndTurn(), "Lethal attack does not softlock turn");
                    UnityEngine.Object.Destroy(testRoot);
                    Debug.Log("WP-06 Play Mode checks passed: mouse targeting, feedback, repeat locks, disable recovery, lethal hit and freed-cell movement.");
                    return true;
                }
                Require(controller.TryEndTurn(), "Attack turn ends");
                return false;
            }
            ClickAttack();
            if (attackCount == 2)
            {
                controller.enabled = false;
                Require(!controller.TryAttackSelected(target.Position), "Disabled controller rejects attack");
                controller.enabled = true;
                Require(controller.Turns.State == TurnState.SelectingAction && !controller.Turns.ActionAvailable &&
                    target.CurrentHealth == expectedHealth, "Interrupted presentation keeps damage and spent action exactly once");
            }
            return false;
        }

        private static UnitRuntimeState Spawn(GridModel grid, string id, HexCoordinates position,
            UnitTeam team, UnitDefinition definition = null)
        {
            Require(UnitRuntimeState.TrySpawn(grid, id, definition ?? new UnitDefinition(id, id, 4), position,
                out var unit, team), "Spawn " + id);
            return unit;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("WP-06 failed: " + message);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
