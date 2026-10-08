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
        public ItemDefinition Weapon { get; internal set; }
        public ItemDefinition Armor { get; internal set; }
        public int HealingPotions { get; internal set; }
        public HeroProgression Progression { get; internal set; } = new HeroProgression();
        public int Attack => Definition.Attack + Progression.AttackBonus + (Weapon?.AttackBonus ?? 0);
        public int Defense => Definition.Defense + Progression.DefenseBonus + (Armor?.DefenseBonus ?? 0);
        public int DamageDie => Weapon?.DamageDie ?? Definition.DamageDie;
        internal GuildAdventurer(string id, UnitDefinition definition)
        { Id = id; Definition = definition; Health = definition.MaxHealth; }
    }

    /// <summary>Session-owned roster and rewards. Never stores scene objects or combat units.</summary>
    public sealed class GuildState
    {
        public const int PartySize = 4;
        public const int ResurrectionCost = 30;
        public const int HealingCost = 5;
        public const int MaximumHealingPotions = 2;
        public const int HiringCost = 40;
        public const int MaximumRosterSize = 256;
        private readonly List<GuildAdventurer> roster = new List<GuildAdventurer>();
        private readonly List<GuildAdventurer> candidates = new List<GuildAdventurer>();
        private int nextRecruitNumber = 3;
        private readonly List<string> selected = new List<string>();
        private readonly List<ItemDefinition> inventory = new List<ItemDefinition>();
        private ExpeditionRun activeRun;
        private List<GuildAdventurer> activeParty;
        public IReadOnlyList<GuildAdventurer> Roster { get; }
        public IReadOnlyList<GuildAdventurer> Candidates { get; }
        internal int NextRecruitNumber => nextRecruitNumber;
        public IReadOnlyList<string> SelectedIds { get; }
        public IReadOnlyList<ItemDefinition> Inventory { get; }
        public int Gold { get; private set; }
        public bool IsAway => activeParty != null;
        public bool CanLaunch => !IsAway && selected.Count == PartySize;
        public event Action Changed;

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
            Candidates = candidates.AsReadOnly();
            RefreshCandidates();
        }

        // The board changes only after accepting an expedition result, never on UI open/load.
        private void RefreshCandidates()
        {
            candidates.Clear();
            if (roster.Count >= MaximumRosterSize || nextRecruitNumber > int.MaxValue - 4) return;
            foreach (var definition in HeroDefinitions.Defaults)
                candidates.Add(new GuildAdventurer(definition.Id + "-" + nextRecruitNumber++, definition));
        }

        public bool TryHire(string candidateId)
        {
            var candidate = candidates.Find(a => a.Id == candidateId);
            if (IsAway || candidate == null || Gold < HiringCost || roster.Count >= MaximumRosterSize) return false;
            candidates.Remove(candidate);
            roster.Add(candidate);
            Gold -= HiringCost;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Whether the current gold can restore four living heroes, without future rewards.</summary>
        public bool CanRebuildParty
        {
            get
            {
                int living = 0, bodies = 0;
                foreach (var hero in roster)
                {
                    if (hero.Status == AdventurerStatus.Alive) living++;
                    if (hero.Status == AdventurerStatus.BodyRecovered) bodies++;
                }
                int needed = Math.Max(0, PartySize - living);
                int resurrect = Math.Min(needed, bodies);
                int hire = needed - resurrect;
                return hire <= candidates.Count && hire <= MaximumRosterSize - roster.Count &&
                    Gold >= resurrect * ResurrectionCost + hire * HiringCost;
            }
        }

        public bool TryToggleSelection(string id)
        {
            if (IsAway) return false;
            var adventurer = roster.Find(a => a.Id == id);
            if (adventurer == null || adventurer.Status != AdventurerStatus.Alive) return false;
            if (selected.Remove(id)) { Changed?.Invoke(); return true; }
            if (selected.Count == PartySize) return false;
            selected.Add(id); Changed?.Invoke(); return true;
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
                        unit.CurrentHealth == adventurer.Health && unit.Weapon == adventurer.Weapon &&
                        unit.Armor == adventurer.Armor && unit.HealingPotions == adventurer.HealingPotions &&
                        unit.Experience == adventurer.Progression.Experience &&
                        unit.TrainingAttackBonus == adventurer.Progression.AttackBonus &&
                        unit.TrainingDefenseBonus == adventurer.Progression.DefenseBonus) found = true;
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
                adventurer.HealingPotions = snapshot.HealingPotions;
                adventurer.Progression.AwardExperience(snapshot.ExperienceGained);
                if (!snapshot.Survived)
                {
                    if (snapshot.BodyRecovered)
                    {
                        if (adventurer.Weapon != null) inventory.Add(adventurer.Weapon);
                        if (adventurer.Armor != null) inventory.Add(adventurer.Armor);
                        for (int i = 0; i < adventurer.HealingPotions; i++) inventory.Add(ItemDefinitions.HealingDraught);
                    }
                    adventurer.Weapon = null; adventurer.Armor = null; adventurer.HealingPotions = 0;
                }
                if (!snapshot.Survived) selected.Remove(adventurer.Id);
            }
            Gold = newGold; inventory.AddRange(result.Items);
            activeRun = null; activeParty = null;
            RefreshCandidates();
            Changed?.Invoke();
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
            Changed?.Invoke();
            return true;
        }

        public bool TryHeal(string id)
        {
            var adventurer = roster.Find(a => a.Id == id);
            if (IsAway || adventurer == null || adventurer.Status != AdventurerStatus.Alive ||
                adventurer.Health == adventurer.Definition.MaxHealth || Gold < HealingCost) return false;
            Gold -= HealingCost; adventurer.Health = adventurer.Definition.MaxHealth; Changed?.Invoke(); return true;
        }

        public bool TryChooseUpgrade(string id, int level, HeroUpgrade upgrade)
        {
            var hero = roster.Find(a => a.Id == id);
            if (IsAway || hero == null || hero.Status != AdventurerStatus.Alive || !hero.Progression.TryChoose(level, upgrade))
                return false;
            Changed?.Invoke();
            return true;
        }

        public bool TryEquip(string id, string itemId)
        {
            var hero = roster.Find(a => a.Id == id);
            var item = inventory.Find(i => i.Id == itemId && i.Category != ItemCategory.HealingConsumable);
            if (IsAway || hero == null || hero.Status != AdventurerStatus.Alive || item == null) return false;
            var old = item.Category == ItemCategory.Weapon ? hero.Weapon : hero.Armor;
            if (old == item) return false;
            inventory.Remove(item);
            if (old != null) inventory.Add(old);
            if (item.Category == ItemCategory.Weapon) hero.Weapon = item; else hero.Armor = item;
            Changed?.Invoke(); return true;
        }

        public bool TryUnequip(string id, ItemCategory slot)
        {
            var hero = roster.Find(a => a.Id == id);
            if (IsAway || hero == null || hero.Status != AdventurerStatus.Alive ||
                (slot != ItemCategory.Weapon && slot != ItemCategory.Armor)) return false;
            var item = slot == ItemCategory.Weapon ? hero.Weapon : hero.Armor;
            if (item == null) return false;
            inventory.Add(item);
            if (slot == ItemCategory.Weapon) hero.Weapon = null; else hero.Armor = null;
            Changed?.Invoke(); return true;
        }

        public bool TryTransferPotion(string id, bool toHero)
        {
            var hero = roster.Find(a => a.Id == id);
            if (IsAway || hero == null || hero.Status != AdventurerStatus.Alive) return false;
            if (toHero)
            {
                if (hero.HealingPotions >= MaximumHealingPotions || !inventory.Remove(ItemDefinitions.HealingDraught)) return false;
                hero.HealingPotions++;
            }
            else
            {
                if (hero.HealingPotions == 0) return false;
                hero.HealingPotions--; inventory.Add(ItemDefinitions.HealingDraught);
            }
            Changed?.Invoke(); return true;
        }

        internal static GuildState Restore(GuildSaveData data)
        {
            if (data == null || data.version < 1 || data.version > GuildSaveData.CurrentVersion || data.gold < 0 ||
                data.roster == null || data.roster.Length < 8 || data.roster.Length > MaximumRosterSize ||
                (data.version < 3 && data.roster.Length != 8) || data.selected == null ||
                data.selected.Length > PartySize || data.items == null || data.items.Length > 100000)
                throw new ArgumentException("Invalid guild save.");
            var guild = new GuildState(data.gold);
            if (data.version >= 3)
            {
                if (data.nextRecruitNumber < 7 || data.candidates == null || data.candidates.Length > 4)
                    throw new ArgumentException("Invalid recruitment board.");
                guild.nextRecruitNumber = data.nextRecruitNumber;
            }
            guild.roster.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in data.roster)
            {
                var hero = saved == null ? null : RestoreIdentity(saved.id, saved.definition, guild.nextRecruitNumber,
                    data.version >= 3);
                if (hero == null || !seen.Add(saved.id) || saved.definition != hero.Definition.Id ||
                    saved.status < 0 || saved.status > (int)AdventurerStatus.Lost ||
                    saved.health < 0 || saved.health > hero.Definition.MaxHealth ||
                    ((saved.status == (int)AdventurerStatus.Alive) != (saved.health > 0)))
                    throw new ArgumentException("Invalid saved adventurer.");
                hero.Health = saved.health; hero.Status = (AdventurerStatus)saved.status;
                if (data.version >= 4) hero.Progression = HeroProgression.Restore(saved.experience, saved.upgrades);
                guild.roster.Add(hero);
                if (data.version >= 2)
                {
                    hero.Weapon = RestoreItem(saved.weapon, ItemCategory.Weapon);
                    hero.Armor = RestoreItem(saved.armor, ItemCategory.Armor);
                    if (saved.potions < 0 || saved.potions > MaximumHealingPotions ||
                        (hero.Status != AdventurerStatus.Alive && (hero.Weapon != null || hero.Armor != null || saved.potions != 0)))
                        throw new ArgumentException("Invalid saved loadout.");
                    hero.HealingPotions = saved.potions;
                }
            }
            // All initial records remain, including lost heroes; saves cannot erase their deaths.
            foreach (var definition in HeroDefinitions.Defaults)
                for (int copy = 1; copy <= 2; copy++)
                    if (!seen.Contains(definition.Id + "-" + copy)) throw new ArgumentException("Missing initial adventurer.");
            if (data.version >= 3)
            {
                guild.candidates.Clear();
                var classes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var saved in data.candidates)
                {
                    var candidate = saved == null ? null : RestoreIdentity(saved.id, saved.definition, guild.nextRecruitNumber, true);
                    if (candidate == null || !seen.Add(candidate.Id) || !classes.Add(candidate.Definition.Id) ||
                        !TryReadRecruitNumber(candidate.Id, candidate.Definition.Id, out int number) || number < 3)
                        throw new ArgumentException("Invalid saved candidate.");
                    guild.candidates.Add(candidate);
                }
            }
            guild.selected.Clear(); seen.Clear();
            foreach (var id in data.selected)
            {
                var hero = guild.roster.Find(a => a.Id == id);
                if (hero == null || hero.Status != AdventurerStatus.Alive || !seen.Add(id))
                    throw new ArgumentException("Invalid saved party.");
                guild.selected.Add(id);
            }
            foreach (var id in data.items)
            {
                ItemDefinition found = null;
                foreach (var item in ItemDefinitions.All) if (item.Id == id) found = item;
                if (found == null) throw new ArgumentException("Unknown saved item.");
                guild.inventory.Add(found);
            }
            return guild;
        }

        private static GuildAdventurer RestoreIdentity(string id, string definitionId, int nextNumber, bool allowRecruits)
        {
            foreach (var definition in HeroDefinitions.Defaults)
                if (definition.Id == definitionId && TryReadRecruitNumber(id, definitionId, out int number) &&
                    number > 0 && (number <= 2 || (allowRecruits && number < nextNumber)))
                    return new GuildAdventurer(id, definition);
            return null;
        }

        private static bool TryReadRecruitNumber(string id, string definition, out int number)
        {
            number = 0;
            return id != null && id.StartsWith(definition + "-", StringComparison.Ordinal) &&
                int.TryParse(id.Substring(definition.Length + 1), out number) &&
                id == definition + "-" + number;
        }

        private static ItemDefinition RestoreItem(string id, ItemCategory category)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var item in ItemDefinitions.All) if (item.Id == id && item.Category == category) return item;
            throw new ArgumentException("Unknown or invalid equipped item.");
        }
    }
}
