using ArabicSupport.Core;
using ArabicSupport.Utils;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArabicSupport.Patches
{
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(GUIContent) })]
    public static class Patch_WidgetsLabelGUIContent
    {
        // Harmony only recognizes a single parameter named "__state" for
        // passing data from Prefix to Postfix. To carry more than one piece
        // of information (the anchor AND the original text), it needs to be
        // bundled into one struct like this.
        private struct LabelState
        {
            public TextAnchor Anchor;
            public string OriginalText;
        }

        public static void Prefix(Rect rect, GUIContent content, out LabelState __state)
        {
            __state = new LabelState { Anchor = Text.Anchor, OriginalText = null };

            if (content == null || string.IsNullOrEmpty(content.text) || !ArabicDetector.ContainsArabic(content.text))
                return;

            // GUIContent objects are sometimes reused across frames rather
            // than recreated each time. Overwriting content.text without
            // saving the original would permanently bake the wrapped version
            // into that object. Save it here so Postfix can put it back.
            __state.OriginalText = content.text;

            content.text = FullPipeline.Process(content.text, rect.width, Text.Font);

            if (content.text.Contains("\n"))
            {
                if (Text.Anchor == TextAnchor.UpperLeft)
                    Text.Anchor = TextAnchor.UpperRight;
                else if (Text.Anchor == TextAnchor.MiddleLeft)
                    Text.Anchor = TextAnchor.MiddleRight;
                else if (Text.Anchor == TextAnchor.LowerLeft)
                    Text.Anchor = TextAnchor.LowerRight;
            }
        }

        public static void Postfix(GUIContent content, LabelState __state)
        {
            Text.Anchor = __state.Anchor;

            // Restore the original text so the (possibly reused) GUIContent
            // object is left exactly as it was found.
            if (__state.OriginalText != null && content != null)
            {
                content.text = __state.OriginalText;
            }
        }
    }
}
