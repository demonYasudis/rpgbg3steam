using System;
using UnityEngine;

namespace GuildTactics.Units
{
    /// <summary>Presentation-only mapping; existing gameplay IDs and balance remain unchanged.</summary>
    public sealed class UnitSpriteSheet : IDisposable
    {
        public enum Pose { Idle, Walk, Attack, Defend, Hit }
        private readonly Sprite[][] frames = new Sprite[5][];
        public string AssetName { get; }

        public static string AssetFor(string id)
        {
            switch (id)
            {
                case "warrior": return "knight";
                case "rogue": return "thief";
                case "ranger": return "archer";
                case "mage": return "mage";
                case "ash-crawler": return "wolf";
                case "veil-stalker": return "goblin";
                case "hollow-brute": return "orc";
                case "crypt-warden": return "knight";
                case "cinder-keeper": return "mage";
                default: return null;
            }
        }

        public static UnitSpriteSheet Load(string id)
        {
            string asset = AssetFor(id);
            if (asset == null) return null;
            var texture = Resources.Load<Texture2D>("UnitSprites/" + asset);
            return texture == null ? null : new UnitSpriteSheet(asset, texture);
        }

        private UnitSpriteSheet(string asset, Texture2D texture)
        {
            AssetName = asset;
            // Generated sheets are not uniform atlases. Row boundaries are measured in source pixels.
            int[] boundaries = asset == "wolf"
                ? new[] { 0, 160, 310, 475, 625, 780 }
                : asset == "knight" ? new[] { 0, 145, 280, 445, 590, 730 }
                : new[] { 0, 145, 295, 445, 590, 730 };
            for (int row = 0; row < frames.Length; row++)
            {
                int count = asset == "goblin" && row >= 1 && row <= 4 ? 9 : 8;
                // Wolf's fifth row includes death: use only the first four living hit poses.
                int used = asset == "wolf" && row == 4 ? 4 : count;
                frames[row] = new Sprite[used];
                for (int column = 0; column < used; column++)
                {
                    float x0 = Mathf.Round(column * texture.width / (float)count);
                    float x1 = Mathf.Round((column + 1) * texture.width / (float)count);
                    float top = boundaries[row] * texture.height / 1024f;
                    float bottom = boundaries[row + 1] * texture.height / 1024f;
                    frames[row][column] = Sprite.Create(texture,
                        new Rect(x0, texture.height - bottom, x1 - x0, bottom - top),
                        new Vector2(0.5f, 0.45f), 145f * texture.height / 1024f,
                        0, SpriteMeshType.FullRect);
                    frames[row][column].name = asset + "_" + (Pose)row + "_" + column;
                }
            }
        }

        public Sprite Frame(Pose pose, float elapsed, bool loop = true)
        {
            var sequence = frames[(int)pose];
            int index = Mathf.Max(0, Mathf.FloorToInt(elapsed * (pose == Pose.Idle ? 5f : 10f)));
            return sequence[loop ? index % sequence.Length : Mathf.Min(index, sequence.Length - 1)];
        }

        public void Dispose()
        {
            foreach (var sequence in frames)
                foreach (var frame in sequence)
                    if (Application.isPlaying) UnityEngine.Object.Destroy(frame);
                    else UnityEngine.Object.DestroyImmediate(frame);
            // Resources owns the shared texture. Do not destroy it with an individual unit.
        }
    }
}
