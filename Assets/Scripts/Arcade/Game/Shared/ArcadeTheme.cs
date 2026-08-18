using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// The arcade's visual language in one place: near black panels, warm brass edges and
    /// cream text, taken from the chess screens that came first.
    ///
    /// The UI is built in code rather than from USS, so without somewhere central to put
    /// these every game would carry its own slightly different copy of the same colours.
    /// Six games in, that is the difference between one product and six.
    /// </summary>
    public static class ArcadeTheme
    {
        public static readonly Color Ink = new Color(0.05f, 0.05f, 0.06f, 0.82f);
        public static readonly Color Scrim = new Color(0.03f, 0.03f, 0.04f, 0.86f);
        public static readonly Color Text = new Color(0.92f, 0.90f, 0.84f);
        public static readonly Color TextBright = new Color(0.96f, 0.94f, 0.88f);
        public static readonly Color TextDim = new Color(0.62f, 0.58f, 0.52f);
        public static readonly Color Brass = new Color(0.38f, 0.30f, 0.20f, 1f);
        public static readonly Color BrassBright = new Color(0.74f, 0.58f, 0.33f, 1f);

        public static Label Heading(string text, float size = 15f)
        {
            var label = new Label(text);
            label.style.color = TextDim;
            label.style.fontSize = size;
            label.style.letterSpacing = 2f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 6f;
            return label;
        }

        public static Label Body(string text, float size = 13f)
        {
            var label = new Label(text);
            label.style.color = Text;
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public static Button MakeButton(string text, System.Action onClick, float width = 0f)
        {
            var button = new Button(onClick) { text = text };
            button.style.paddingLeft = 14f;
            button.style.paddingRight = 14f;
            button.style.paddingTop = 8f;
            button.style.paddingBottom = 8f;
            button.style.marginTop = 4f;
            button.style.marginBottom = 0f;
            button.style.fontSize = 14f;
            button.style.color = Text;
            Round(button, 4f);
            SetBorderWidth(button, 1f);
            if (width > 0f) button.style.width = width;

            // Applies the colours as well as the hover and press states.
            UiButtonFeedback.Apply(button);
            return button;
        }

        /// <summary>A panel that reads as a physical plate rather than floating text.</summary>
        public static VisualElement Panel(float padding = 14f)
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = Ink;
            panel.style.paddingLeft = padding;
            panel.style.paddingRight = padding;
            panel.style.paddingTop = padding * 0.85f;
            panel.style.paddingBottom = padding * 0.85f;
            Round(panel, 6f);
            Border(panel, Brass, 1f);
            return panel;
        }

        public static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        public static void Border(VisualElement element, Color color, float width)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            SetBorderWidth(element, width);
        }

        public static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }
    }
}
