using System;
using System.Linq;
using GuildTactics.Generation;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class UnitSpriteChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Generated Unit Sprites")]
        public static void Run()
        {
            var definitions = HeroDefinitions.Defaults.Concat(EnemyDefinitions.Regular.Select(a => a.Unit))
                .Concat(new[] { EnemyDefinitions.MiniBoss.Unit });
            foreach (var definition in definitions)
            {
                using (var sheet = UnitSpriteSheet.Load(definition.Id))
                {
                    Require(sheet != null, "Missing sheet for " + definition.Id);
                    foreach (UnitSpriteSheet.Pose pose in Enum.GetValues(typeof(UnitSpriteSheet.Pose)))
                        for (int i = 0; i < 9; i++)
                        {
                            var frame = sheet.Frame(pose, i / 10f, false);
                            Require(frame != null && frame.rect.width > 0 && frame.rect.height > 0, "Valid frame");
                            Require(frame.rect.xMin >= 0 && frame.rect.yMin >= 0 &&
                                frame.rect.xMax <= frame.texture.width && frame.rect.yMax <= frame.texture.height,
                                "Frame within source texture");
                            Require(frame.texture.filterMode == FilterMode.Point && frame.texture.mipmapCount == 1,
                                "Point filtering without mipmaps");
                        }
                }
            }
            Require(UnitSpriteSheet.Load("unknown-content") == null, "Procedural fallback for unknown IDs");
            PixelPresentationChecks.Run();
            UnitMovementChecks.Run();
            CombatChecks.Run();
            AbilityChecks.Run();
            VisibilityChecks.Run();
            Debug.Log("Generated unit sprite checks passed: all nine archetypes, seven textures, animation frames, movement, combat, abilities and visibility.");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Unit sprites: " + message); }
    }
}
