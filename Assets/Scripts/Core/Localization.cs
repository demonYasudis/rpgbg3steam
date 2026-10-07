using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildTactics.Core
{
    public enum InterfaceLanguage { English, Russian }

    /// <summary>Presentation-only language preference. Content IDs and saves remain language-independent.</summary>
    public static class Localization
    {
        public const string PreferenceKey = "GuildTactics.InterfaceLanguage";
        public static InterfaceLanguage Language { get; private set; } = InterfaceLanguage.English;
        public static void LoadPreference() => SetLanguage(ParsePreference(PlayerPrefs.GetString(PreferenceKey, "ru")), false);
        public static InterfaceLanguage ParsePreference(string value) => value == "en" ? InterfaceLanguage.English : InterfaceLanguage.Russian;
        public static void SetLanguage(InterfaceLanguage value, bool persist = true)
        {
            if (!Enum.IsDefined(typeof(InterfaceLanguage), value)) throw new ArgumentOutOfRangeException(nameof(value));
            Language = value;
            if (persist) { PlayerPrefs.SetString(PreferenceKey, value == InterfaceLanguage.Russian ? "ru" : "en"); PlayerPrefs.Save(); }
        }
        public static string T(string english)
        {
            if (english == null || Language == InterfaceLanguage.English) return english;
            return Russian.TryGetValue(english, out var translated) ? translated : english;
        }
        public static string F(string english, params object[] values) => string.Format(T(english), values);
        public static string AdventurerName(string id)
        {
            if (Language == InterfaceLanguage.English || string.IsNullOrEmpty(id)) return id;
            int separator = id.LastIndexOf('-');
            if (separator < 1) return id;
            string name = id.Substring(0, separator);
            string title = char.ToUpperInvariant(name[0]) + name.Substring(1);
            return Russian.ContainsKey(title) ? T(title) + " " + id.Substring(separator + 1) : id;
        }
        public static string SaveMessage(string message)
        {
            const string prefix = "Could not save guild: ";
            return message != null && message.StartsWith(prefix, StringComparison.Ordinal)
                ? T(prefix) + message.Substring(prefix.Length) : T(message);
        }
        // Combat feedback is stored in its original form so an open result also switches language.
        public static string CombatMessage(string message)
        {
            if (message == null || Language == InterfaceLanguage.English) return message;
            message = T(message);
            if (message.StartsWith("Cinder burst\n", StringComparison.Ordinal))
                message = T("Cinder burst") + message.Substring("Cinder burst".Length);
            foreach (var ability in Units.HeroDefinitions.Defaults)
                foreach (var definition in ability.Abilities)
                    if (message.StartsWith(definition.Name, StringComparison.Ordinal))
                    { message = T(definition.Name) + message.Substring(definition.Name.Length); break; }
            return System.Text.RegularExpressions.Regex.Replace(message, @"\b(HIT|MISS|DEAD|DEF|HP|TRAP|ARMED|PIT|damage)\b",
                match => T(match.Value));
        }
        private static readonly Dictionary<string, string> Russian = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Storage:\n", "Склад:\n" },
            { "Recruitment - 20 gold per hero", "Найм — 20 золота за героя" },
            { "Candidates refresh after returning from an expedition. Offers and hires are saved.", "Кандидаты обновляются после возвращения из похода. Предложения и найм сохраняются." },
            { "Hire (20)", "Нанять (20)" },
            { "Not enough heroes and gold to rebuild a party. Start a new guild to continue.", "Не хватает героев и золота для восстановления отряда. Начните новую гильдию, чтобы продолжить." },
            { "Select replacements from the reserve when someone dies.\nFewer than four living heroes: hire candidates, resurrect bodies or start a new guild.", "Выбирайте замену погибшим из резерва.\nЕсли живых меньше четырёх: наймите кандидатов, воскресите героев или начните новую гильдию." },
            { "Level {0}/5 | XP {1}/{2} | Upgrades {3} | Training ATK +{4}, DEF +{5}", "Уровень {0}/5 | Опыт {1}/{2} | Улучшения {3} | Обучение АТК +{4}, ЗАЩ +{5}" },
            { "Train attack +1", "Улучшить атаку +1" }, { "Train defense +1", "Улучшить защиту +1" },
            { "Survivors gain 100 XP on extraction, 25 on retreat. Dead heroes gain none; resurrection keeps training.", "Выжившим: 100 опыта за эвакуацию, 25 за отступление. Погибшим — 0. Воскрешение сохраняет обучение." },
            { "Walls block the line to this target.", "Стена перекрывает линию до цели." },
            { "Crypt Bowman", "Стрелок склепа" },
            { "Cinder burst", "Вспышка углей" },
            { "Cinder burst armed: leave red hexes before the next boss turn.", "Вспышка углей готовится: покиньте красные клетки до следующего хода босса." },
            { "Red hexes: 10 damage next boss turn.", "Красные клетки: 10 урона на ходе босса." },
            { "Previous hero", "Предыдущий герой" },
            { "Next hero", "Следующий герой" },
            { "Loadout: ", "Снаряжение: " },
            { "None", "Нет" },
            { "ATK {0} | DEF {1} | Damage d{2}+{3} | Potions {4}/2", "АТК {0} | ЗАЩ {1} | Урон d{2}+{3} | Зелья {4}/2" },
            { "Weapon: {0}. Equip: ATK {1} -> {2}, d{3} -> d{4} (stock {5})", "Оружие: {0}. Замена: АТК {1} → {2}, d{3} → d{4} (склад: {5})" },
            { "Armor: {0}. Equip: DEF {1} -> {2} (stock {3})", "Броня: {0}. Замена: ЗАЩ {1} → {2} (склад: {3})" },
            { "Equip weapon", "Надеть оружие" }, { "Remove weapon", "Снять оружие" },
            { "Equip armor", "Надеть броню" }, { "Remove armor", "Снять броню" },
            { "Give potion", "Выдать зелье" }, { "Return potion", "Вернуть зелье" },
            { "Potion ({0})", "Зелье ({0})" },
            { "Heal yourself up to 8 HP for one action. Needs a potion and missing health.", "Восстановить себе до 8 ОЗ за одно действие. Нужны зелье и неполное здоровье." },
            { "Healed {0} HP. One potion and one action spent.", "Восстановлено {0} ОЗ. Потрачены одно зелье и действие." },
            { "Equip living heroes before departure. Items must be in storage.\nSurvivors keep their loadout. Recovered bodies return items to storage; lost heroes lose their gear.\nPotions heal only their owner, up to 8 HP, for one action.", "Снаряжайте живых героев до похода. Предметы должны быть на складе.\nВыжившие сохраняют снаряжение. С возвращённого тела вещи идут на склад; с потерянным героем они пропадают.\nЗелье лечит только владельца, до 8 ОЗ, за одно действие." },
            { "Map overview", "Вся карта" },
            { "Character detail", "Персонажи" },
            { "Wheel: zoom | Right drag: pan", "Колесо: масштаб | ПКМ + движение: камера" },
            { "Gold: {0} | Party: {1}/4 | Stored items: {2}", "Золото: {0} | Отряд: {1}/4 | На складе: {2}" },
            { "Dungeon seed: {0} | Combat seed: {1}", "Сид подземелья: {0} | Сид боя: {1}" },
            { " (fallback)", " (резервная карта)" },
            { " | Seed: ", " | Сид: " },
            { "Hover: {0} {1}", "Клетка: {0} {1}" },
            { "Unknown", "Неизвестно" },
            { "Explored", "Исследовано" },
            { "Visible", "Видно" },
            { "Ground", "Земля" },
            { "HighGround", "Возвышенность" },
            { "Pit", "Яма" },
            { "Blocked", "Преграда" },
            { "{0}/{1} HP", "{0}/{1} ОЗ" },
            { "Range {0}. Roll d20 + attack against defense; costs one action.", "Дальность {0}. Бросок d20 + атака против защиты; стоит одно действие." },
            { "Loot: {0} gold / {1} items", "Добыча: {0} золота / {1} предметов" },
            { ": {0}/{1} HP", ": {0}/{1} ОЗ" },
            { "Start a new guild?", "Начать новую гильдию?" },
            { "Current gold, roster and stored items will be replaced. This cannot be undone.", "Золото, герои и предметы будут заменены. Это действие нельзя отменить." },
            { "Confirm new guild", "Создать гильдию" },
            { "Cancel", "Отмена" },
            { "ADVENTURERS' GUILD", "ГИЛЬДИЯ ИСКАТЕЛЕЙ ПРИКЛЮЧЕНИЙ" },
            { "Choose four living adventurers. Wounds persist; healing costs 5 gold.", "Выберите четырёх живых героев. Раны сохраняются; лечение стоит 5 золота." },
            { "Remove", "Убрать" },
            { "Select", "Выбрать" },
            { "DEAD — body recovered", "ПОГИБ — тело возвращено" },
            { "PERMANENTLY LOST", "ПОТЕРЯН НАВСЕГДА" },
            { "Resurrect (30)", "Воскресить (30)" },
            { "Heal (5)", "Лечить (5)" },
            { "Choose an expedition — crypt ruins", "Выберите экспедицию — руины склепа" },
            { "Possible reward: ", "Возможная награда: " },
            { "Next seed: ", "Следующий сид: " },
            { "Start ", "Начать: " },
            { "Select four living adventurers to depart.", "Для похода выберите четырёх живых героев." },
            { "Successful extraction recovers bodies on reachable ground.\nBodies in pits and all bodies after defeat are permanently lost.", "При эвакуации возвращаются тела на доступных клетках.\nТела в ямах и все тела после поражения теряются навсегда." },
            { "Select replacements from the reserve when someone dies.\nFewer than four living heroes: resurrect recovered bodies or start a new guild.", "Погибших можно заменить героями из резерва.\nМеньше четырёх живых героев? Воскресите погибших или создайте новую гильдию." },
            { "Storage (equipment use comes later):\n", "Склад (использование снаряжения появится позже):\n" },
            { "New guild...", "Новая гильдия..." },
            { "Guild progress saves automatically. Quitting during an expedition restores the guild before departure.", "Прогресс гильдии сохраняется автоматически. Выход во время экспедиции вернёт состояние до похода." },
            { "Move / cancel", "Движение / отмена" },
            { "Click a green hex to move. Cancel a selected attack or ability.", "Нажмите на зелёную клетку для движения. Отменяет выбранную атаку или способность." },
            { "> Basic attack", "> Обычная атака" },
            { "Basic attack", "Обычная атака" },
            { "VICTORY - All enemies defeated", "ПОБЕДА — Все враги побеждены" },
            { "DEFEAT - The party has fallen", "ПОРАЖЕНИЕ — Отряд погиб" },
            { "Round ", "Раунд " },
            { "Enemy turn  |  ", "Ход врага  |  " },
            { "Preparing next turn", "Подготовка следующего хода" },
            { "ENEMY TURN — please wait", "ХОД ВРАГА — подождите" },
            { "YOUR TURN — ", "ВАШ ХОД — " },
            { "End turn", "Конец хода" },
            { "Finish this hero's turn, even if movement or action remain.", "Завершить ход героя, даже если остались очки движения или действие." },
            { "Wait", "Ждать" },
            { "Spend your action without attacking. You may still move.", "Потратить действие без атаки. Движение остаётся доступным." },
            { "Gold hex: active hero · Green: move · Purple: target", "Золото: активный герой · Зелёный: движение · Фиолетовый: цель" },
            { "Return / retreat", "Отступить" },
            { "Last combat result", "Итог атаки" },
            { "Retreat to the guild? Living heroes keep their wounds. All collected loot and dead adventurers are permanently lost.", "Отступить в гильдию? Раны живых героев сохранятся. Вся добыча и погибшие герои будут потеряны навсегда." },
            { "No combat result yet.", "Результатов боя пока нет." },
            { "Confirm retreat and loss", "Отступить с потерями" },
            { "Back to battle", "Назад в бой" },
            { "EMPTY", "ПУСТО" },
            { "CHEST", "СУНДУК" },
            { "EXIT", "ВЫХОД" },
            { "BODY", "ТЕЛО" },
            { "LOST", "УТРАЧЕНО" },
            { "Unreachable bodies will be permanently lost.", "Тела на недоступных клетках будут потеряны навсегда." },
            { "Confirm permanent loss", "Подтвердить потерю" },
            { "Find CHEST. Open it from the same or an adjacent visible hex (1 action).", "Найдите СУНДУК. Откройте его со своей или соседней видимой клетки (1 действие)." },
            { "Area cleared. Bring one survivor to EXIT to extract the party.", "Враги побеждены. Приведите выжившего к ВЫХОДУ для эвакуации отряда." },
            { "Loot collected. Defeat remaining enemies, then return to EXIT.", "Добыча собрана. Победите оставшихся врагов и вернитесь к ВЫХОДУ." },
            { "RELIC", "РЕЛИКВИЯ" },
            { "TARGET", "ЦЕЛЬ" },
            { "Take relic", "Взять реликвию" },
            { "Recover relic", "Добыть реликвию" },
            { "Eliminate target", "Устранить цель" },
            { "Clear area", "Зачистить область" },
            { "Marked quarry", "Отмеченная цель" },
            { "Objective complete. Reach EXIT to extract.", "Задание выполнено. Доберитесь до ВЫХОДА." },
            { "Find and defeat TARGET, then return to EXIT.", "Найдите и уничтожьте ЦЕЛЬ, затем вернитесь к ВЫХОДУ." },
            { "Clear area: {0} enemies remaining", "Зачистка: осталось врагов — {0}" },
            { "Complete the mission, then stand on EXIT.", "Выполните задание и встаньте на ВЫХОД." },
            { "Unrecovered bodies will be permanently lost.", "Невосстановленные тела будут потеряны навсегда." },
            { "20–40 gold, weapon or armor, healing draught", "20–40 золота, оружие или броня, лечебное зелье" },
            { "30–60 gold, weapon or armor, healing draught", "30–60 золота, оружие или броня, лечебное зелье" },
            { "40–70 gold, weapon or armor, healing draught", "40–70 золота, оружие или броня, лечебное зелье" },
            { "Find RELIC. Take it from the same or an adjacent visible hex (1 action).", "Найдите РЕЛИКВИЮ. Возьмите её со своей или соседней видимой клетки (1 действие)." },
            { "Bodies on reachable ground are recovered only after all enemies are defeated.\nExtraction from an uncleared area permanently loses dead heroes.", "Тела на доступных клетках возвращаются только после полной зачистки.\nВыход из незачищенной области навсегда теряет погибших героев." },
            { "Open chest", "Открыть сундук" },
            { "Stand next to the visible chest with one action remaining.", "Встаньте рядом с видимым сундуком, сохранив одно действие." },
            { "Extract party", "Эвакуация" },
            { "Open the chest, defeat every enemy, then stand on EXIT.", "Откройте сундук, победите всех врагов и встаньте на ВЫХОД." },
            { "EXPEDITION COMPLETE", "ЭКСПЕДИЦИЯ ЗАВЕРШЕНА" },
            { "PARTY RETREATED", "ОТРЯД ОТСТУПИЛ" },
            { "EXPEDITION LOST", "ЭКСПЕДИЦИЯ ПРОВАЛЕНА" },
            { "\nGold recovered: ", "\nДобыто золота: " },
            { "weapon, d", "оружие, d" },
            { ", attack +", ", атака +" },
            { "armor, defense +", "броня, защита +" },
            { "healing consumable, ", "лечебное зелье, " },
            { " HP", " ОЗ" },
            { ": DEAD — body recovered", ": ПОГИБ — тело возвращено" },
            { ": PERMANENTLY LOST", ": ПОТЕРЯН НАВСЕГДА" },
            { "\nRecovered bodies can be resurrected in the guild for 30 gold.", "\nГероев с возвращёнными телами можно воскресить в гильдии за 30 золота." },
            { "Expedition result", "Результат экспедиции" },
            { "Return to guild", "Вернуться в гильдию" },
            { "Choose an action or move to a green hex.", "Выберите действие или перейдите на зелёную клетку." },
            { "Choose an enemy within basic attack range.", "Выберите врага в радиусе обычной атаки." },
            { "Explore closer first: this hex is outside current vision.", "Подойдите ближе: эта клетка вне поля зрения." },
            { "Cannot walk onto walls or pits.", "Нельзя ходить сквозь стены или по ямам." },
            { "That ally acts on their own turn. Choose a free green hex.", "Этот союзник действует в свой ход. Выберите свободную зелёную клетку." },
            { "Action already used. Move or end your turn.", "Действие уже потрачено. Двигайтесь или завершите ход." },
            { "No movement left. Use an action or end your turn.", "Очков движения нет. Используйте действие или завершите ход." },
            { "No path within your movement allowance. Choose a green hex.", "Не хватает очков движения для этого пути. Выберите зелёную клетку." },
            { "ready", "готово" },
            { "used", "потрачено" },
            { "HP: health. Defense: attack total needed to hit. Move: hex movement points. One attack or ability per turn.", "ОЗ: здоровье. Защита: нужный результат атаки. Движение: очки перемещения. Одна атака или способность за ход." },
            { "Warrior", "Воин" },
            { "Rogue", "Плут" },
            { "Ranger", "Следопыт" },
            { "Mage", "Маг" },
            { "Heavy Strike", "Тяжёлый удар" },
            { "Push", "Толчок" },
            { "Backstab", "Удар в спину" },
            { "Evade", "Уклонение" },
            { "Aimed Shot", "Прицельный выстрел" },
            { "Trap", "Ловушка" },
            { "Fire Burst", "Огненная вспышка" },
            { "Blink", "Скачок" },
            { "Range 1. Weapon hit: +4 damage, -2 accuracy.", "Дальность 1. Удар оружием: +4 урона, −2 к точности." },
            { "Range 1. Push one hex away onto free ground or into a fatal pit.", "Дальность 1. Толчок на клетку назад: на свободную землю или в смертельную яму." },
            { "Range 1. +5 damage if another ally is adjacent to the target.", "Дальность 1. +5 урона, если рядом с целью есть другой союзник." },
            { "+4 defense until your next turn. Click your own hex.", "+4 к защите до следующего хода. Нажмите на свою клетку." },
            { "Range 6. Weapon hit: +3 accuracy, +2 damage.", "Дальность 6. Выстрел: +3 к точности, +2 урона." },
            { "Range 3. Empty hex: 8 damage on hostile entry. One active trap; allies safe.", "Дальность 3. Пустая клетка: 8 урона наступившему врагу. Одна ловушка; союзники не страдают." },
            { "Range 4, radius 1. Roll a weapon hit against each enemy; allies safe.", "Дальность 4, радиус 1. Бросок атаки по каждому врагу; союзники не страдают." },
            { "Range 3. Teleport to free ground, crossing obstacles; keep movement points.", "Дальность 3. Телепортация на свободную клетку через препятствия; очки движения сохраняются." },
            { "Outer crypt", "Внешний склеп" },
            { "Deep crypt", "Глубины склепа" },
            { "Low risk · 2–3 enemies · no keeper", "Низкий риск · 2–3 врага · без хранителя" },
            { "Dangerous · 3–5 enemies · possible keeper", "Опасно · 3–5 врагов · возможен хранитель" },
            { "20–60 gold, weapon or armor, healing draught", "20–60 золота, оружие или броня, лечебное зелье" },
            { "Iron Edge", "Железный клинок" },
            { "Crypt Mail", "Кольчуга склепа" },
            { "Healing Draught", "Лечебное зелье" },
            { "Crypt Sentinel", "Часовой склепа" },
            { "Crypt Guard", "Страж склепа" },
            { "Ash Crawler", "Пепельный ползун" },
            { "Crypt Warden", "Смотритель склепа" },
            { "Veil Stalker", "Сумрачный охотник" },
            { "Hollow Brute", "Пустотный громила" },
            { "Cinder Keeper", "Хранитель углей" },
            { "No action available for this unit.", "У этого героя нет доступного действия." },
            { "This unit does not know that ability.", "Эта способность герою неизвестна." },
            { "Target is out of range.", "Цель вне дальности действия." },
            { "Target is outside current vision.", "Цель вне поля зрения." },
            { "Choose a living enemy.", "Выберите живого врага." },
            { "Backstab needs another ally next to the enemy.", "Для удара в спину рядом с врагом нужен другой союзник." },
            { "Push needs an adjacent enemy and free ground or a pit behind it.", "Для толчка нужен соседний враг и свободная клетка или яма за ним." },
            { "Choose your own hex.", "Выберите свою клетку." },
            { "Choose free ground without a trap.", "Выберите свободную клетку без ловушки." },
            { "Choose free ground.", "Выберите свободную клетку." },
            { "The burst must reach at least one enemy.", "Вспышка должна задеть хотя бы одного врага." },
            { "Unsupported ability.", "Способность не поддерживается." },
            { "Recovered the previous guild checkpoint from backup.", "Предыдущее состояние гильдии восстановлено из резервной копии." },
            { "Save unavailable or incompatible. A fresh guild is open; old files are kept until the next successful save.", "Сохранение недоступно или несовместимо. Создана новая гильдия; старые файлы сохранятся до следующего успешного сохранения." },
            { "Could not save guild: ", "Не удалось сохранить гильдию: " },
            { "HIT", "ПОПАДАНИЕ" },
            { "MISS", "ПРОМАХ" },
            { " | DEAD", " | ПОГИБ" },
            { " / PIT / DEAD", " / ЯМА / ПОГИБ" },
            { " / DEF +", " / ЗАЩ +" },
            { " / ARMED: ", " / УСТАНОВЛЕНА: " },
            { " damage", " урона" },
            { " HIT", " ПОПАДАНИЕ" },
            { " MISS", " ПРОМАХ" },
            { " DEAD", " ПОГИБ" },
            { "\nTRAP -", "\nЛОВУШКА −" },
            { " HP / DEAD", " ОЗ / ПОГИБ" },
            { "TRAP ", "ЛОВУШКА " },
            { " HP / DEAD\n", " ОЗ / ПОГИБ\n" },
            { " HP\n", " ОЗ\n" },
            { "Vision debug ON", "Отладка зрения ВКЛ" },
            { "Vision debug OFF", "Отладка зрения ВЫКЛ" },
            { "Enemy out of reach. Basic attack range: {0}. Move closer.", "Враг вне досягаемости. Дальность атаки: {0}. Подойдите ближе." },
            { "Basic attack: range {0}. Choose a highlighted enemy.", "Обычная атака: дальность {0}. Выберите подсвеченного врага." },
            { "Chest opened: {0} gold and {1} items. Return to EXIT after combat.", "Сундук открыт: {0} золота и {1} предметов. Вернитесь к ВЫХОДУ после боя." },
            { "HP {0}/{1}   Defense {2}   Move {3}   Action {4}", "ОЗ {0}/{1}   Защита {2}   Движение {3}   Действие {4}" },
            { "  |  Round ", "  |  Раунд " },
            { "DEAD", "ПОГИБ" },
            { "DEF", "ЗАЩ" },
            { "HP", "ОЗ" },
            { "TRAP", "ЛОВУШКА" },
            { "ARMED", "УСТАНОВЛЕНА" },
            { "PIT", "ЯМА" },
            { "damage", "урона" },
        };
    }
}
