using UnityEngine;

namespace GuildTactics.Units
{
    /// <summary>Original code-authored 16px silhouettes. No imported textures or runtime dependencies.</summary>
    public static class CryptPixelArt
    {
        public const int Size = 16;
        public static Color32[] Draw(string id, Color accent)
        {
            var pixels = new Color32[Size * Size];
            Color32 outline = new Color32(19, 20, 29, 255);
            Color32 metal = new Color32(157, 174, 177, 255);
            Color32 skin = new Color32(217, 184, 143, 255);
            Color32 light = new Color32(246, 220, 148, 255);
            void Rect(int x, int y, int w, int h, Color32 c)
            { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) pixels[j * Size + i] = c; }
            // Boots, dark outline, tunic and face form the common readable humanoid base.
            Rect(4, 1, 3, 4, outline); Rect(9, 1, 3, 4, outline);
            Rect(3, 4, 10, 7, outline); Rect(4, 5, 8, 5, accent);
            Rect(5, 10, 6, 5, outline); Rect(6, 11, 4, 3, skin);
            Rect(4, 5, 8, 1, metal);
            switch (id)
            {
                case "warrior":
                    Rect(5, 13, 6, 2, metal); Rect(7, 11, 2, 3, metal);
                    Rect(1, 5, 4, 6, outline); Rect(2, 6, 2, 4, metal);
                    Rect(13, 5, 1, 9, metal); Rect(12, 6, 3, 1, light); break;
                case "rogue":
                    Rect(4, 12, 8, 3, accent); Rect(5, 10, 6, 2, outline);
                    Rect(6, 12, 4, 1, skin); Rect(1, 6, 2, 4, metal); Rect(13, 6, 2, 4, metal); break;
                case "ranger":
                    Rect(4, 14, 8, 1, accent); Rect(5, 13, 6, 1, accent);
                    Rect(13, 4, 1, 9, light); Rect(14, 6, 1, 5, light);
                    Rect(12, 4, 1, 9, outline); break;
                case "mage":
                    Rect(4, 1, 8, 5, accent); Rect(4, 12, 8, 1, accent);
                    Rect(6, 13, 4, 2, accent); Rect(7, 15, 2, 1, light);
                    Rect(1, 2, 1, 10, metal); Rect(0, 11, 3, 3, light); break;
                case "ash-crawler":
                    pixels = new Color32[Size * Size];
                    Rect(3, 5, 10, 5, outline); Rect(4, 6, 8, 3, accent);
                    for (int x = 1; x <= 13; x += 4) { Rect(x, 3, 2, 4, metal); Rect(x, 9, 2, 3, metal); }
                    Rect(5, 8, 2, 1, light); Rect(9, 8, 2, 1, light); break;
                case "crypt-warden":
                    Rect(5, 11, 6, 4, metal); Rect(6, 12, 1, 1, outline); Rect(9, 12, 1, 1, outline);
                    Rect(1, 3, 1, 12, metal); Rect(0, 12, 3, 2, light); break;
                case "veil-stalker":
                    Rect(3, 2, 10, 9, accent); Rect(4, 11, 8, 3, outline);
                    Rect(5, 14, 6, 1, accent); Rect(6, 12, 4, 1, light); break;
                case "hollow-brute":
                    Rect(1, 4, 14, 7, outline); Rect(2, 5, 12, 5, accent);
                    Rect(4, 10, 8, 4, skin); Rect(5, 12, 2, 1, outline); Rect(9, 12, 2, 1, outline); break;
                case "cinder-keeper":
                    Rect(2, 3, 12, 8, accent); Rect(4, 12, 8, 2, metal);
                    Rect(4, 14, 2, 2, light); Rect(7, 14, 2, 2, light); Rect(10, 14, 2, 2, light);
                    Rect(7, 6, 2, 4, light); Rect(0, 4, 2, 8, metal); break;
            }
            return pixels;
        }
    }
}
