using System;
using System.Linq;
using GuildTactics.Expeditions;
using GuildTactics.Meta;
using UnityEngine;
using UnityEngine.UIElements;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.UI
{
    public sealed partial class GuildPartyScreen
    {
        private ItemCategory? storageFilter;
        private string storageItemId;
        private Vector2 storageScroll, equipmentScroll;
        private string storageFeedbackEn, storageFeedbackRu;

        private void RememberStorageScroll()
        {
            var items = shell?.Q<ScrollView>("storage-scroll");
            var equipment = shell?.Q<ScrollView>("equipment-scroll");
            if (items != null) storageScroll = items.scrollOffset;
            if (equipment != null) equipmentScroll = equipment.scrollOffset;
        }

        private void BuildStorage(VisualElement parent)
        {
            var layout = Row("storage-layout"); layout.name = "guild-storage"; parent.Add(layout);
            var equipment = new ScrollView(ScrollViewMode.Vertical) { name = "equipment-scroll" };
            equipment.AddToClassList("gallery-scroll"); equipment.AddToClassList("storage-equipment"); layout.Add(equipment);
            AddPanelLabel(equipment, Text("ADVENTURER & EQUIPMENT", "ГЕРОЙ И СНАРЯЖЕНИЕ"), "equipment-title");
            var choices = guild.Roster.Select(h => L.AdventurerName(h.Id) + " · " + h.Id).ToList();
            int selected = Math.Max(0, guild.Roster.ToList().FindIndex(h => h.Id == selectedId));
            var heroes = new DropdownField(choices, selected) { name = "storage-hero" };
            heroes.RegisterValueChangedCallback(evt =>
            {
                int index = choices.IndexOf(evt.newValue);
                if (index >= 0) { SelectHero(guild.Roster[index].Id); ClearStorageFeedback(); }
            });
            equipment.Add(heroes);
            var hero = guild.Roster.First(h => h.Id == selectedId);
            var portrait = new VisualElement();
            portrait.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("UI/Art/" + hero.Definition.Id));
            portrait.AddToClassList("storage-portrait"); equipment.Add(portrait);
            MakeMovingPortrait(portrait);
            AddPanelLabel(equipment, L.T(hero.Definition.DisplayName) + " · HP " + hero.Health + "/" + hero.Definition.MaxHealth + " · " + Status(hero), "storage-hero-status");
            AddPanelLabel(equipment, Text("ATK ", "АТК ") + hero.Attack + Text(" · DEF ", " · ЗАЩ ") + hero.Defense + " · d" + hero.DamageDie, "storage-hero-stats");
            BuildEquipmentSlot(equipment, hero, ItemCategory.Weapon, hero.Weapon);
            BuildEquipmentSlot(equipment, hero, ItemCategory.Armor, hero.Armor);
            var potions = Row("equipment-slot"); equipment.Add(potions);
            potions.Add(new ItemIcon(ItemCategory.HealingConsumable));
            AddPanelLabel(potions, Text("Potions ", "Зелья ") + hero.HealingPotions + "/" + GuildState.MaximumHealingPotions, "equipped-potions");
            StorageCommand(equipment, Text("Return one potion", "Вернуть одно зелье"), "storage-return-potion",
                HeroEquipmentReason(hero) ?? (hero.HealingPotions == 0 ? Text("No potions carried.", "У героя нет зелий.") : null),
                () => guild.TryTransferPotion(hero.Id, false));
            equipment.schedule.Execute(() => equipment.scrollOffset = equipmentScroll);

            var warehouse = Row("storage-warehouse"); layout.Add(warehouse);
            AddPanelLabel(warehouse, Text("GUILD STORAGE", "СКЛАД ГИЛЬДИИ") + " · " + guild.Inventory.Count, "storage-title");
            var filters = Row("storage-filters"); warehouse.Add(filters);
            AddStorageFilter(filters, null, Text("All", "Все"));
            foreach (ItemCategory category in Enum.GetValues(typeof(ItemCategory))) AddStorageFilter(filters, category, CategoryName(category));
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "storage-scroll" };
            scroll.AddToClassList("gallery-scroll"); scroll.AddToClassList("storage-items"); warehouse.Add(scroll);
            var grid = Row("storage-grid"); scroll.Add(grid);
            // Identical copies share one tile. Inventory still owns each copy; no synthetic items are created.
            var counts = guild.Inventory.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.Count());
            var visible = ItemDefinitions.All.Where(i => !storageFilter.HasValue || i.Category == storageFilter.Value).ToList();
            if (visible.All(i => i.Id != storageItemId)) storageItemId = visible.FirstOrDefault(i => counts.ContainsKey(i.Id) || Owners(i).Length > 0)?.Id;
            int tiles = 0;
            foreach (var item in visible)
            {
                counts.TryGetValue(item.Id, out int count);
                string owners = Owners(item);
                if (count == 0 && owners.Length == 0) continue;
                tiles++;
                var tile = AddButton(grid, "", () => { storageItemId = item.Id; ClearStorageFeedback(); dirty = true; }, "storage-item-" + item.Id);
                tile.AddToClassList("storage-item"); tile.EnableInClassList("selected", storageItemId == item.Id);
                tile.Add(new ItemIcon(item.Category));
                AddPanelLabel(tile, L.T(item.Name), "item-name-" + item.Id);
                AddPanelLabel(tile, Text("Stored: ", "На складе: ") + count, "item-count-" + item.Id);
                if (owners.Length > 0) AddPanelLabel(tile, Text("Equipped", "У героев"), "item-owned-" + item.Id);
                tile.tooltip = L.T(item.Name) + " · " + ItemEffect(item);
            }
            if (guild.Inventory.Count == 0) AddPanelLabel(scroll, Text("Storage is empty. Earn loot in expeditions or return equipment here.",
                "Склад пуст. Получайте добычу в походах или снимите снаряжение с героя."), "storage-empty");
            else if (tiles == 0) AddPanelLabel(scroll, Text("No items of this type.", "Нет предметов этого типа."), "storage-filter-empty");
            scroll.schedule.Execute(() => scroll.scrollOffset = storageScroll);
            var details = new ScrollView(ScrollViewMode.Vertical) { name = "storage-details" };
            details.AddToClassList("gallery-scroll"); details.AddToClassList("storage-details"); warehouse.Add(details);
            if (storageFeedbackEn != null) AddPanelLabel(details, Text(storageFeedbackEn, storageFeedbackRu), "storage-feedback");
            var chosen = visible.FirstOrDefault(i => i.Id == storageItemId);
            if (chosen == null) AddPanelLabel(details, Text("Select an item to compare its effect.", "Выберите вещь, чтобы сравнить её эффект."), "storage-hint");
            else
            {
                AddPanelLabel(details, L.T(chosen.Name) + " · " + ItemEffect(chosen), "storage-item-effect");
                AddPanelLabel(details, Comparison(hero, chosen), "storage-comparison");
                string owners = Owners(chosen);
                if (owners.Length > 0) AddPanelLabel(details, Text("Carried by: ", "У героев: ") + owners, "storage-owners");
                counts.TryGetValue(chosen.Id, out int available);
                string reason = HeroEquipmentReason(hero);
                if (reason == null && chosen.Category == ItemCategory.HealingConsumable && hero.HealingPotions >= GuildState.MaximumHealingPotions)
                    reason = Text("Both potion slots are full.", "Оба места для зелий заняты.");
                if (reason == null && (chosen.Category == ItemCategory.Weapon ? hero.Weapon : chosen.Category == ItemCategory.Armor ? hero.Armor : null) == chosen)
                    reason = Text("Already equipped. Remove it to return it to storage.", "Уже надето. Снимите вещь, чтобы вернуть её на склад.");
                if (reason == null && available == 0) reason = Text("No free copies. Remove the item from its owner first.", "Нет свободных экземпляров. Сначала снимите вещь с владельца.");
                StorageCommand(details, chosen.Category == ItemCategory.HealingConsumable ? Text("Give one potion", "Выдать одно зелье") : Text("Equip", "Надеть"),
                    "storage-equip", reason, () => chosen.Category == ItemCategory.HealingConsumable ? guild.TryTransferPotion(hero.Id, true) : guild.TryEquip(hero.Id, chosen.Id));
            }
        }

        private void AddStorageFilter(VisualElement parent, ItemCategory? category, string title)
        {
            var button = AddButton(parent, title, () => { storageFilter = category; var scroll = shell.Q<ScrollView>("storage-scroll");
                if (scroll != null) scroll.scrollOffset = Vector2.zero; ClearStorageFeedback(); dirty = true; },
                "storage-filter-" + (category.HasValue ? category.Value.ToString() : "all"));
            button.EnableInClassList("selected", storageFilter == category);
        }

        private void BuildEquipmentSlot(VisualElement parent, GuildAdventurer hero, ItemCategory category, ItemDefinition item)
        {
            var row = Row("equipment-slot"); parent.Add(row); row.Add(new ItemIcon(category));
            AddPanelLabel(row, CategoryName(category) + ": " + (item == null ? Text("Empty", "Пусто") : L.T(item.Name)), "equipped-" + category);
            StorageCommand(parent, Text("Remove ", "Снять: ") + CategoryName(category), "storage-remove-" + category,
                HeroEquipmentReason(hero) ?? (item == null ? Text("Slot is empty.", "Слот пуст.") : null), () => guild.TryUnequip(hero.Id, category));
        }

        private string HeroEquipmentReason(GuildAdventurer hero) => guild.IsAway ? Text("Party is away.", "Отряд в походе.") :
            hero.Status != AdventurerStatus.Alive ? Text("Only living heroes can change equipment.", "Менять снаряжение могут только живые герои.") : null;

        private void StorageCommand(VisualElement parent, string title, string name, string reason, Func<bool> command)
        {
            var owner = guild;
            string heroId = selectedId;
            var button = AddButton(parent, title, () =>
            {
                if (commandPending) return;
                commandPending = true;
                foreach (var control in shell.Query<Button>().ToList()) control.SetEnabled(false);
                shell.Q<DropdownField>("storage-hero")?.SetEnabled(false);
                bool applied = ReferenceEquals(owner, guild) && heroId == selectedId && command();
                storageFeedbackEn = applied ? "Equipment updated." : "Command declined. Availability has been refreshed.";
                storageFeedbackRu = applied ? "Снаряжение обновлено." : "Команда отклонена. Доступность действий обновлена.";
                restoreFocus = name; dirty = true;
            }, name);
            button.SetEnabled(string.IsNullOrEmpty(reason)); button.tooltip = reason;
            if (!string.IsNullOrEmpty(reason)) AddPanelLabel(parent, reason, name + "-reason");
        }

        private void ClearStorageFeedback() { storageFeedbackEn = null; storageFeedbackRu = null; }
        private string Owners(ItemDefinition item) => string.Join(", ", guild.Roster.Where(h => h.Weapon == item || h.Armor == item ||
            (item.Category == ItemCategory.HealingConsumable && h.HealingPotions > 0)).Select(h => L.AdventurerName(h.Id) + " [" + h.Id + "]"));
        private string CategoryName(ItemCategory category) => category == ItemCategory.Weapon ? Text("Weapons", "Оружие") :
            category == ItemCategory.Armor ? Text("Armor", "Броня") : Text("Potions", "Зелья");
        private string ItemEffect(ItemDefinition item) => item.Category == ItemCategory.Weapon ? Text("ATK +", "АТК +") + item.AttackBonus + " · d" + item.DamageDie :
            item.Category == ItemCategory.Armor ? Text("DEF +", "ЗАЩ +") + item.DefenseBonus : Text("Heal up to ", "Лечит до ") + item.Healing + " HP";
        private string Comparison(GuildAdventurer hero, ItemDefinition item)
        {
            if (item.Category == ItemCategory.Weapon) return Text("ATK ", "АТК ") + hero.Attack + " → " +
                (hero.Definition.Attack + hero.TrainingAttack + item.AttackBonus) + Text(" · Damage d", " · Урон d") + hero.DamageDie + " → d" + item.DamageDie;
            if (item.Category == ItemCategory.Armor) return Text("DEF ", "ЗАЩ ") + hero.Defense + " → " + (hero.Definition.Defense + hero.TrainingDefense + item.DefenseBonus);
            return Text("Carried: ", "С собой: ") + hero.HealingPotions + "/" + GuildState.MaximumHealingPotions + Text(" · One action in combat; heals the owner.", " · Одно действие в бою; лечит владельца.");
        }
    }
}
