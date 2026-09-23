using System;
using System.Collections.Generic;
using GuildTactics.Combat;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Units;

namespace GuildTactics.Expeditions
{
    /// <summary>One chest and one extraction. Commands share the authoritative combat turn permissions.</summary>
    public sealed class ExpeditionRun
    {
        private readonly TurnManager turns;
        private readonly List<UnitRuntimeState> party = new List<UnitRuntimeState>();
        private readonly int seed;
        public HexCoordinates Chest { get; }
        public HexCoordinates Extraction { get; }
        public bool ChestOpened { get; private set; }
        public int CollectedGold { get; private set; }
        public IReadOnlyList<ItemDefinition> CollectedItems { get; private set; } = Array.Empty<ItemDefinition>();
        public ExpeditionResult Result { get; private set; }
        public IReadOnlyList<UnitRuntimeState> Party { get; }

        public IReadOnlyList<AdventurerBody> Bodies
        {
            get
            {
                var bodies = new List<AdventurerBody>();
                var reachable = DungeonValidator.Distances(turns.Grid, Extraction);
                foreach (var unit in party)
                    if (!unit.IsAlive) bodies.Add(new AdventurerBody(unit.InstanceId, unit.Position,
                        reachable.ContainsKey(unit.Position)));
                return bodies.AsReadOnly();
            }
        }
        public bool HasUnrecoverableBodies
        {
            get { foreach (var body in Bodies) if (!body.Recoverable) return true; return false; }
        }

        public ExpeditionRun(DungeonMap map, TurnManager turns)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            this.turns = turns ?? throw new ArgumentNullException(nameof(turns));
            if (!ReferenceEquals(map.Grid, turns.Grid)) throw new ArgumentException("Expedition and turns must share a grid.");
            foreach (var unit in turns.Order) if (unit.Team == UnitTeam.Player) party.Add(unit);
            if (party.Count != 4) throw new ArgumentException("An expedition requires four adventurers.");
            Party = party.AsReadOnly();
            seed = map.Seed; Chest = map.Objective; Extraction = map.PlayerSpawns[0];
            if (Chest == Extraction || !DungeonValidator.Distances(map.Grid, Extraction).ContainsKey(Chest))
                throw new ArgumentException("Chest and extraction must be distinct and connected.");
        }

        private bool CanInteract(UnitRuntimeState actor) => Result == null &&
            actor != null && actor.Team == UnitTeam.Player && party.Contains(actor) &&
            actor.IsPlacedOn(turns.Grid) && turns.CanSelectAction(actor);

        public bool CanOpenChest(UnitRuntimeState actor) => CanInteract(actor) && !ChestOpened &&
            turns.ActionAvailable && turns.CanSee(actor, Chest) && actor.Position.DistanceTo(Chest) <= 1;

        public bool TryOpenChest(UnitRuntimeState actor)
        {
            if (!CanOpenChest(actor) || !turns.TryBeginAction(actor)) return false;
            // Independent stream: reward does not depend on battle rolls or opening time.
            var random = new GenerationRandom(GenerationRandom.ParseSeed(
                "loot:" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            CollectedGold = 20 + random.Next(41);
            CollectedItems = Array.AsReadOnly(new[]
            {
                random.Next(2) == 0 ? ItemDefinitions.Weapon : ItemDefinitions.Armor,
                ItemDefinitions.HealingDraught
            });
            ChestOpened = true;
            turns.TryCompleteAction(actor);
            return true;
        }

        public bool CanExtract(UnitRuntimeState actor) => CanInteract(actor) && ChestOpened &&
            actor.Position == Extraction && BattleRules.Evaluate(turns.Order) == BattleOutcome.Victory;

        public bool TryExtract(UnitRuntimeState actor, bool confirmAbandonment = false)
        {
            if (!CanExtract(actor) || (HasUnrecoverableBodies && !confirmAbandonment)) return false;
            // One survivor reaching the entrance brings out the entire surviving party.
            // The cleared area allows automatic recovery of every body connected to the exit.
            var recovered = new HashSet<string>();
            foreach (var body in Bodies) if (body.Recoverable) recovered.Add(body.InstanceId);
            Result = new ExpeditionResult(seed, ExpeditionOutcome.Extracted, CollectedGold, CollectedItems, party, recovered);
            return true;
        }

        public void RefreshOutcome()
        {
            if (Result != null || turns.State == TurnState.Moving || turns.State == TurnState.ResolvingAction) return;
            if (BattleRules.Evaluate(turns.Order) == BattleOutcome.Defeat)
                Result = new ExpeditionResult(seed, ExpeditionOutcome.Defeated, 0, Array.Empty<ItemDefinition>(), party);
        }
    }

    public sealed class AdventurerBody
    {
        public string InstanceId { get; }
        public HexCoordinates Position { get; }
        public bool Recoverable { get; }
        internal AdventurerBody(string id, HexCoordinates position, bool recoverable)
        { InstanceId = id; Position = position; Recoverable = recoverable; }
    }
}
