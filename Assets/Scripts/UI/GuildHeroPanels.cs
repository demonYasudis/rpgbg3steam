using System;
using System.Linq;
using GuildTactics.Meta;
using UnityEngine.UIElements;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.UI
{
    public sealed partial class GuildPartyScreen
    {
        private bool detailsFromReserve;
        private bool commandPending;
        private string feedbackEn, feedbackRu;
        public bool IsHeroDetailsOpen => IsOpen && dialog == GuildDialog.Hero;

        private void OpenHeroDetails(string id, bool fromReserve)
        {
            if (guild.Roster.All(h => h.Id != id)) return;
            selectedId = id; detailsFromReserve = fromReserve;
            dialog = GuildDialog.Hero; ClearFeedback();
            restoreFocus = "close-hero-panel"; dirty = true;
        }

        private void OpenRecruitment()
        {
            dialog = GuildDialog.Recruitment; ClearFeedback();
            restoreFocus = "close-hero-panel"; dirty = true;
        }

        private void CloseHeroPanel()
        {
            if (dialog == GuildDialog.Hero && detailsFromReserve)
            { dialog = GuildDialog.Reserve; restoreFocus = "reserve-details-" + selectedId; }
            else if (dialog == GuildDialog.NewGuild)
            { dialog = GuildDialog.Recruitment; restoreFocus = "new-guild"; }
            else
            {
                restoreFocus = dialog == GuildDialog.Hero ? "open-hero" : "open-recruitment";
                dialog = GuildDialog.None;
            }
            ClearFeedback(); dirty = true;
        }

        private void ClearFeedback() { feedbackEn = null; feedbackRu = null; }

        private VisualElement HeroPanel(string title, string description)
        {
            var close = UiComponents.Button(Text("Back / Esc", "Назад / Esc"), CloseHeroPanel);
            close.name = "close-hero-panel";
            modal = UiComponents.Modal(title, description, close);
            var panel = modal.Q("dialog"); panel.AddToClassList("hero-dialog");
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "hero-panel-scroll" };
            scroll.AddToClassList("gallery-scroll"); scroll.AddToClassList("hero-panel-scroll");
            panel.Insert(2, scroll);
            content.SetEnabled(false); shell.Add(modal);
            if (feedbackEn != null) AddPanelLabel(scroll, Text(feedbackEn, feedbackRu), "command-feedback");
            return scroll;
        }

        private void BuildHeroDetails()
        {
            var hero = guild.Roster.FirstOrDefault(h => h.Id == selectedId);
            if (hero == null) { dialog = GuildDialog.None; return; }
            var body = HeroPanel(L.AdventurerName(hero.Id), L.T(hero.Definition.DisplayName) + " · " + Status(hero));
            AddPanelLabel(body, "HP " + hero.Health + "/" + hero.Definition.MaxHealth + " · " + Text("Gold: ", "Золото: ") + guild.Gold, "hero-health");
            AddPanelLabel(body, L.F("ATK {0} | DEF {1} | Damage d{2}+{3} | Potions {4}/2",
                hero.Attack, hero.Defense, hero.DamageDie, hero.Definition.DamageBonus, hero.HealingPotions), "hero-stats");
            AddPanelLabel(body, L.F("Level {0}/5 | XP {1}/{2} | Upgrades {3} | Training ATK +{4}, DEF +{5}",
                hero.Level, hero.Experience, HeroProgression.NextThreshold(hero.Experience), hero.AvailableUpgrades,
                hero.TrainingAttack, hero.TrainingDefense), "hero-progression");
            bool alive = hero.Status == AdventurerStatus.Alive;
            string deadReason = hero.Status == AdventurerStatus.Lost ? Text("Permanently lost heroes cannot return.", "Потерянный навсегда герой не может вернуться.") :
                Text("Resurrect this hero first.", "Сначала воскресите героя.");
            string healReason = !alive ? deadReason : hero.Health == hero.Definition.MaxHealth ? Text("Already at full health.", "Здоровье уже полное.") :
                guild.Gold < GuildState.HealingCost ? NeedGold(GuildState.HealingCost) : "";
            AddCommand(body, Text("Heal", "Лечение") + " · " + GuildState.HealingCost + Text(" gold", " золота"), "hero-heal", healReason,
                () => guild.TryHeal(hero.Id), "Hero healed.", "Герой вылечен.");
            string resurrectReason = hero.Status == AdventurerStatus.Lost ? deadReason : alive ? Text("This hero is alive.", "Герой жив.") :
                guild.Gold < GuildState.ResurrectionCost ? NeedGold(GuildState.ResurrectionCost) : "";
            AddCommand(body, Text("Resurrect", "Воскрешение") + " · " + GuildState.ResurrectionCost + Text(" gold", " золота"), "hero-resurrect", resurrectReason,
                () => guild.TryResurrect(hero.Id), "Hero resurrected. Add them to the party from reserve.", "Герой воскрешён. Добавьте его в отряд из резерва.");
            string upgradeReason = !alive ? deadReason : hero.AvailableUpgrades == 0 ? Text("No unspent upgrades. Gain experience in expeditions.", "Нет свободных улучшений. Получайте опыт в походах.") : "";
            AddCommand(body, Text("Train attack +1 · 1 upgrade", "Атака +1 · 1 улучшение"), "hero-upgrade-attack", upgradeReason,
                () => guild.TryUpgrade(hero.Id, HeroUpgrade.Attack), "Attack increased.", "Атака повышена.");
            AddCommand(body, Text("Train defense +1 · 1 upgrade", "Защита +1 · 1 улучшение"), "hero-upgrade-defense", upgradeReason,
                () => guild.TryUpgrade(hero.Id, HeroUpgrade.Defense), "Defense increased.", "Защита повышена.");
        }

        private string NeedGold(int cost) => Text("Requires ", "Нужно ") + cost + Text(" gold; available: ", " золота; доступно: ") + guild.Gold + ".";

        private void BuildRecruitment()
        {
            var body = HeroPanel(Text("Recruitment", "Найм"),
                Text("Candidates refresh after returning from an expedition. Hired heroes join the reserve.", "Кандидаты обновляются после возвращения из похода. Нанятые герои попадают в резерв."));
            AddPanelLabel(body, Text("Gold: ", "Золото: ") + guild.Gold + " · " + Text("Roster: ", "Героев: ") + guild.Roster.Count + "/" + GuildState.MaximumRosterSize, "recruitment-gold");
            if (guild.Candidates.Count == 0) AddPanelLabel(body, Text("No candidates left. Return from an expedition to refresh offers.",
                "Кандидатов больше нет. Вернитесь из похода, чтобы обновить предложения."), "recruitment-empty");
            foreach (string candidate in guild.Candidates)
            {
                string id = candidate;
                string reason = guild.Roster.Count >= GuildState.MaximumRosterSize ? Text("Roster is full.", "В гильдии нет мест.") :
                    guild.Gold < GuildState.RecruitmentCost ? NeedGold(GuildState.RecruitmentCost) : "";
                AddCommand(body, L.AdventurerName(id) + " · " + Text("Hire", "Нанять") + " · " + GuildState.RecruitmentCost + Text(" gold", " золота"),
                    "hire-" + id, reason, () =>
                    {
                        if (!guild.TryHire(id)) return false;
                        selectedId = id; return true;
                    }, "Hero hired into the reserve.", "Герой нанят в резерв.");
            }
            if (!guild.CanRebuildParty)
            {
                AddPanelLabel(body, Text("Not enough heroes and gold to rebuild a party. You can start a new guild.",
                    "Недостаточно героев и золота для восстановления отряда. Можно начать новую гильдию."), "recovery-reason");
                AddButton(body, Text("New guild…", "Новая гильдия…"), () =>
                { dialog = GuildDialog.NewGuild; restoreFocus = "close-hero-panel"; dirty = true; }, "new-guild");
            }
        }

        private void BuildNewGuildConfirmation()
        {
            var body = HeroPanel(Text("Start a new guild?", "Начать новую гильдию?"),
                Text("Current gold, roster and stored items will be replaced. This cannot be undone.", "Текущие золото, герои и предметы будут заменены. Это нельзя отменить."));
            AddCommand(body, Text("Confirm new guild", "Создать новую гильдию"), "confirm-new-guild", "",
                () => bootstrap.TryStartNewGuild(), "New guild created.", "Новая гильдия создана.");
        }

        private void AddCommand(VisualElement parent, string title, string name, string reason, Func<bool> command, string successEn, string successRu)
        {
            var row = new VisualElement(); row.AddToClassList("hero-command"); parent.Add(row);
            var button = AddButton(row, title, () =>
            {
                if (commandPending) return;
                commandPending = true;
                // Disable the retained controls immediately, before Changed schedules a rebuild.
                // A second click/submit in this frame cannot spend another upgrade or payment.
                foreach (var control in modal.Query<Button>().ToList()) control.SetEnabled(false);
                bool applied = command();
                feedbackEn = applied ? successEn : "Command declined. Current state and availability have been refreshed.";
                feedbackRu = applied ? successRu : "Команда отклонена. Состояние и доступность действий обновлены.";
                restoreFocus = name; dirty = true;
            }, name);
            button.SetEnabled(string.IsNullOrEmpty(reason));
            button.tooltip = reason;
            if (!string.IsNullOrEmpty(reason)) AddPanelLabel(row, reason, name + "-reason");
        }

        private static void AddPanelLabel(VisualElement parent, string text, string name)
        {
            var label = new Label(text) { name = name }; label.AddToClassList("hero-panel-label"); parent.Add(label);
        }
    }
}
