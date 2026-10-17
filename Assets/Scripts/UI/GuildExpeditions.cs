using System.Linq;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using UnityEngine.UIElements;
using L = GuildTactics.Core.Localization;

namespace GuildTactics.UI
{
    public sealed partial class GuildPartyScreen
    {
        private string travelFeedbackEn, travelFeedbackRu;

        private void BuildExpeditions(VisualElement parent)
        {
            var page = shell;
            var layout = Row("travel-layout"); layout.name = "guild-expeditions"; parent.Add(layout);
            var offers = new ScrollView(ScrollViewMode.Vertical) { name = "travel-offers" };
            offers.AddToClassList("gallery-scroll"); offers.AddToClassList("travel-offers"); layout.Add(offers);
            var grid = Row("travel-grid"); offers.Add(grid);
            for (int i = 0; i < ExpeditionSelection.Offers.Count; i++)
            {
                int index = i;
                var offer = ExpeditionSelection.Offers[i];
                bool selected = index == bootstrap.Expeditions.SelectedIndex;
                var card = AddButton(grid, "", () =>
                {
                    if (commandPending || !ReferenceEquals(shell, page) || dialog != GuildDialog.None || guild.IsAway) return;
                    bool chosen = index == bootstrap.Expeditions.SelectedIndex || bootstrap.TrySelectExpedition(index);
                    travelFeedbackEn = chosen ? null : "Selection declined. Check the current party and expedition.";
                    travelFeedbackRu = chosen ? null : "Выбор отклонён. Проверьте отряд и поход.";
                    restoreFocus = "expedition-" + index; dirty = true;
                }, "expedition-" + index);
                card.AddToClassList("travel-card"); card.EnableInClassList("selected", selected);
                card.Add(new ExpeditionIllustration(offer.Biome, offer.Mission.Type));
                TravelLabel(card, L.T(offer.Name), "offer-title-" + index, "travel-title");
                TravelLabel(card, L.T(offer.Mission.Name), "offer-mission-" + index);
                TravelLabel(card, L.T(offer.Difficulty), "offer-risk-" + index);
                TravelLabel(card, Text("Sections: ", "Участков: ") + offer.Sections + " · " +
                    (selected ? Text("Selected", "Выбрано") : Text("Choose", "Выбрать")), "offer-selection-" + index, "travel-selection");
            }

            var briefing = Row("travel-briefing"); layout.Add(briefing);
            var details = new ScrollView(ScrollViewMode.Vertical) { name = "travel-details" };
            details.AddToClassList("gallery-scroll"); details.AddToClassList("travel-details"); briefing.Add(details);
            var selectedOffer = bootstrap.Expeditions.Selected;
            TravelLabel(details, L.T(selectedOffer.Name), "travel-selected-name", "travel-title");
            TravelLabel(details, Objective(selectedOffer.Mission.Type), "travel-objective");
            TravelLabel(details, L.T(selectedOffer.Difficulty) + " · " + Text("Sections: ", "Участков: ") + selectedOffer.Sections, "travel-risk");
            var mission = selectedOffer.Mission;
            TravelLabel(details, Text("Per section on successful return: ", "За участок при успешном возврате: ") + mission.MinimumGold + "–" + mission.MaximumGold +
                Text(" gold", " золота") + (mission.GrantsItems ? Text(", a weapon or armor, a healing draught.", ", оружие либо броня, лечебное зелье.") : "."), "travel-reward");
            TravelLabel(details, Text("DEPARTING PARTY", "ОТРЯД В ПОХОД") + " · " + guild.SelectedIds.Count + "/4", "travel-party-title");
            var party = Row("travel-party"); details.Add(party);
            for (int i = 0; i < Meta.GuildState.PartySize; i++)
            {
                var row = Row("travel-hero"); party.Add(row);
                var hero = i < guild.SelectedIds.Count ? guild.Roster.First(h => h.Id == guild.SelectedIds[i]) : null;
                if (hero == null) TravelLabel(row, Text("Empty slot — select an adventurer", "Пустое место — выберите бойца"), "travel-empty-" + i);
                else
                {
                    row.Add(Miniature(hero));
                    var description = new VisualElement(); description.style.alignSelf = Align.Stretch; row.Add(description);
                    TravelLabel(description, L.AdventurerName(hero.Id), "travel-hero-" + hero.Id);
                    TravelLabel(description, "HP " + hero.Health + "/" + hero.Definition.MaxHealth, "travel-health-" + hero.Id, "travel-note");
                    row.tooltip = hero.Id + " · " + L.T(hero.Definition.DisplayName) + " · " + Status(hero);
                }
            }
            var actions = Row("travel-actions"); briefing.Add(actions);
            if (travelFeedbackEn != null) TravelLabel(actions, Text(travelFeedbackEn, travelFeedbackRu), "travel-feedback");
            TravelLabel(actions, LaunchReason(), "travel-launch-reason", "travel-note");
            var back = AddButton(actions, Text("Edit party / Esc", "Изменить отряд / Esc"), ReturnFromExpeditions, "travel-back");
            back.tooltip = Text("Return to the party without changing the expedition selection.", "Вернуться к отряду, сохранив выбранный поход.");
            // Capture identity as well as the offer. A retained control cannot launch another guild/choice.
            var owner = guild; var selection = bootstrap.Expeditions; int expectedIndex = selection.SelectedIndex;
            var start = AddButton(actions, Text("Depart", "Отправиться"), () =>
            {
                if (commandPending || !ReferenceEquals(shell, page) || dialog != GuildDialog.None) return;
                commandPending = true;
                foreach (var button in page.Query<Button>().ToList()) button.SetEnabled(false);
                bool current = ReferenceEquals(owner, bootstrap.Guild) && ReferenceEquals(selection, bootstrap.Expeditions) && selection.SelectedIndex == expectedIndex;
                if (current && guild.CanLaunch && bootstrap.TryLaunchExpedition()) { travelFeedbackEn = travelFeedbackRu = null; Hide(); }
                else
                {
                    travelFeedbackEn = current ? "Departure declined. Check the party and try again." : "Selection changed. Review the expedition before departing.";
                    travelFeedbackRu = current ? "Отправка отклонена. Проверьте отряд и повторите попытку." : "Выбор изменился. Проверьте поход перед отправкой.";
                    restoreFocus = "launch-expedition"; dirty = true;
                }
            }, "launch-expedition");
            start.AddToClassList("primary"); start.SetEnabled(guild.CanLaunch);
        }

        private void ReturnFromExpeditions()
        { tab = 0; restoreFocus = "choose-expedition"; travelFeedbackEn = travelFeedbackRu = null; dirty = true; }

        private string Objective(MissionType type)
        {
            switch (type)
            {
                case MissionType.RecoverRelic: return Text("Recover the relic, then reach EXIT with an active survivor.", "Заберите реликвию и приведите активного выжившего к ВЫХОДУ.");
                case MissionType.EliminateTarget: return Text("Find and defeat the marked target, then reach EXIT.", "Найдите и уничтожьте отмеченную цель, затем доберитесь до ВЫХОДА.");
                case MissionType.ClearArea: return Text("Defeat every enemy, then reach EXIT with an active survivor.", "Победите всех врагов и приведите активного выжившего к ВЫХОДУ.");
                default: return Text("Collect the loot, clear the area and reach EXIT.", "Заберите добычу, зачистите область и доберитесь до ВЫХОДА.");
            }
        }

        private static void TravelLabel(VisualElement parent, string text, string name, string css = null)
        {
            var label = new Label(text) { name = name, pickingMode = PickingMode.Ignore }; label.AddToClassList("travel-label");
            if (css != null) label.AddToClassList(css); parent.Add(label);
        }
    }
}
