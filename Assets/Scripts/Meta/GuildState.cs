using System;
using System.Collections.Generic;
using GuildTactics.Expeditions;
using GuildTactics.Units;

namespace GuildTactics.Meta
{
    public enum AdventurerStatus { Alive, BodyRecovered, Lost }

    public sealed class GuildAdventurer
    {
        public string Id { get; }
        public UnitDefinition Definition { get; }
        public int Health { get; internal set; }
        public AdventurerStatus Status { get; internal set; }
        internal GuildAdventurer(string id, UnitDefinition definition)
        { Id = id; Definition = definition; Health = definition.MaxHealth; }
    }

    /// <summary>Session-owned roster and rewards. Never stores scene objects or combat units.</summary>
    public sealed class GuildState
    {
        public const int PartySize = 4;
        public const int ResurrectionCost = 30;
        public const int HealingCost = 5;
        private readonly List<GuildAdventurer> roster = new List<GuildAdventurer>();
        private readonly List<string> selected = new List<string>();
        private readonly List<ItemDefinition> inventory = new List<ItemDefinition>();
        private ExpeditionRun activeRun;
        private List<GuildAdventurer> activeParty;
        public IReadOnlyList<GuildAdventurer> Roster { get; }
        public IReadOnlyList<string> SelectedIds { get; }
        public IReadOnlyList<ItemDefinition> Inventory { get; }
        public int Gold { get; private set; }
        public bool IsAway => activeParty != null;
        public bool CanLaunch => !IsAway && selected.Count == PartySize;

        public GuildState(int startingGold = 100)
        {
            if (startingGold < 0) throw new ArgumentOutOfRangeException(nameof(startingGold));
            Gold = startingGold;
            for (int copy = 0; copy < 2; copy++)
                foreach (var definition in HeroDefinitions.Defaults)
                {
                    var adventurer = new GuildAdventurer(definition.Id + "-" + (copy + 1), definition);
                    roster.Add(adventurer);
                    if (copy == 0) selected.Add(adventurer.Id);
                }
            Roster = roster.AsReadOnly(); SelectedIds = selected.AsReadOnly(); Inventory = inventory.AsReadOnly();
        }

        public bool TryToggleSelection(string id)
        {
            if (IsAway) return false;
            var adventurer = roster.Find(a => a.Id == id);
            if (adventurer == null || adventurer.Status != AdventurerStatus.Alive) return false;
            if (selected.Remove(id)) return true;
            if (selected.Count == PartySize) return false;
            selected.Add(id); return true;
        }

        public IReadOnlyList<GuildAdventurer> BeginExpedition()
        {
            if (!CanLaunch) throw new InvalidOperationException("Select four living adventurers first.");
            activeParty = selected.ConvertAll(id => roster.Find(a => a.Id == id));
            return activeParty.AsReadOnly();
        }

        public void AttachRun(ExpeditionRun run)
        {
            if (!IsAway || activeRun != null || run == null || run.Result != null)
                throw new InvalidOperationException("No pending expedition to attach.");
            if (run.Party.Count != PartySize) throw new ArgumentException("Party mismatch.");
            foreach (var adventurer in activeParty)
            {
                bool found = false;
                foreach (var unit in run.Party)
                    if (unit.InstanceId == adventurer.Id && ReferenceEquals(unit.Definition, adventurer.Definition) &&
                        unit.CurrentHealth == adventurer.Health) found = true;
                if (!found) throw new ArgumentException("Party mismatch.");
            }
            activeRun = run;
        }

        // Only startup failures may release the reservation without an expedition result.
        public void CancelLaunch()
        {
            if (activeRun != null) throw new InvalidOperationException("An active expedition cannot be cancelled.");
            activeParty = null;
        }

        public bool TryReturn()
        {
            var result = activeRun?.Result;
            if (result == null) return false;
            int newGold = checked(Gold + result.Gold);
            foreach (var snapshot in result.Adventurers)
            {
                var adventurer = activeParty.Find(a => a.Id == snapshot.InstanceId);
                adventurer.Health = snapshot.Health;
                adventurer.Status = snapshot.Survived ? AdventurerStatus.Alive :
                    snapshot.BodyRecovered ? AdventurerStatus.BodyRecovered : AdventurerStatus.Lost;
                if (!snapshot.Survived) selected.Remove(adventurer.Id);
            }
            Gold = newGold; inventory.AddRange(result.Items);
            activeRun = null; activeParty = null;
            return true;
        }

        public bool TryResurrect(string id)
        {
            var adventurer = roster.Find(a => a.Id == id);
            if (IsAway || adventurer == null || adventurer.Status != AdventurerStatus.BodyRecovered || Gold < ResurrectionCost)
                return false;
            Gold -= ResurrectionCost;
            adventurer.Health = adventurer.Definition.MaxHealth;
            adventurer.Status = AdventurerStatus.Alive;
            return true;
        }

        public bool TryHeal(string id)
        {
            var adventurer = roster.Find(a => a.Id == id);
            if (IsAway || adventurer == null || adventurer.Status != AdventurerStatus.Alive ||
                adventurer.Health == adventurer.Definition.MaxHealth || Gold < HealingCost) return false;
            Gold -= HealingCost; adventurer.Health = adventurer.Definition.MaxHealth; return true;
        }
    }
}
