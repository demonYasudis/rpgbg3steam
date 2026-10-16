using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildTactics.UI
{
    /// <summary>Reusable, independent elements. Callers own model commands and localization.</summary>
    public static class UiComponents
    {
        public static VisualElement IconLabel(UiIconKind kind, string text)
        {
            var row = new VisualElement(); row.AddToClassList("icon-label");
            row.Add(new UiIcon(kind)); row.Add(new Label(text)); return row;
        }

        public static Button Button(string text, Action clicked, string className = null)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("ui-button");
            if (className != null) button.AddToClassList(className);
            return button;
        }

        public static Button Tab(string text, bool selected, Action clicked)
        {
            var tab = Button(text, clicked, "ui-tab");
            tab.EnableInClassList("selected", selected);
            return tab;
        }

        public static VisualElement Card(string title, string description, Texture2D portrait, Action selected)
        {
            var card = Button("", selected, "ui-card");
            var art = new VisualElement { name = "portrait", pickingMode = PickingMode.Ignore };
            art.AddToClassList("portrait");
            art.style.backgroundImage = new StyleBackground(portrait);
            card.Add(art);
            var heading = new Label(title) { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("card-title");
            card.Add(heading);
            var detail = new Label(description) { pickingMode = PickingMode.Ignore };
            detail.AddToClassList("card-description");
            card.Add(detail);
            return card;
        }

        // Runtime UI Toolkit does not provide the editor's tooltip popup.
        public static void Tooltip(VisualElement target, Label presenter, string text)
        {
            target.tooltip = text;
            target.RegisterCallback<PointerEnterEvent>(_ => presenter.text = text);
            target.RegisterCallback<PointerLeaveEvent>(_ => presenter.text = "");
            target.RegisterCallback<FocusInEvent>(_ => presenter.text = text);
            target.RegisterCallback<FocusOutEvent>(_ => presenter.text = "");
        }

        public static VisualElement Modal(string title, string body, Button close)
        {
            var overlay = new VisualElement { name = "modal-overlay" };
            overlay.AddToClassList("modal-overlay");
            var dialog = new VisualElement { name = "dialog" };
            dialog.AddToClassList("dialog");
            var heading = new Label(title); heading.AddToClassList("title");
            var content = new Label(body); content.AddToClassList("body");
            dialog.Add(heading); dialog.Add(content); dialog.Add(close);
            overlay.Add(dialog);
            overlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            return overlay;
        }
    }
}
