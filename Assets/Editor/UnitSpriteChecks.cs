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
            foreach (var id in new[] { "warrior", "rogue", "ranger", "mage" })
            {
                using (var hero = UnitSpriteSheet.Load(id))
                {
                    Require(hero.AssetName == "hero-" + id + "-v2", "Distinct V2 hero asset: " + id);
                    var first = hero.Frame(UnitSpriteSheet.Pose.Idle, 0);
                    Require(first.rect.size == new Vector2(256, 256), "Regular V2 cell");
                    Require(hero.Frame(UnitSpriteSheet.Pose.Idle, 0.76f).name.EndsWith("_3"), "Four idle poses");
                    Require(hero.Frame(UnitSpriteSheet.Pose.Idle, 1f) == first, "Idle loop duration");
                    Require(hero.Frame(UnitSpriteSheet.Pose.Attack, 0.61f, false).name.EndsWith("_3"), "Attack reaches recovery before action ends");
                    Require(hero.Frame(UnitSpriteSheet.Pose.Hit, 0.31f, false).name.EndsWith("_3"), "Hit reaches recovery before flash ends");
                }
            }
            Require(UnitSpriteSheet.AssetFor("crypt-warden") == "knight" &&
                UnitSpriteSheet.AssetFor("crypt-bowman") == "archer" &&
                UnitSpriteSheet.AssetFor("cinder-keeper") == "mage", "Hero artwork does not replace enemy artwork");
            using (var goblin = UnitSpriteSheet.Load("veil-stalker"))
            {
                var first = goblin.Frame(UnitSpriteSheet.Pose.Idle, 0);
                var ninth = goblin.Frame(UnitSpriteSheet.Pose.Idle, 1.61f, false);
                Require(ninth.name.EndsWith("_8"), "Goblin idle preserves all nine source frames");
                Require(Mathf.Approximately(first.rect.width / first.texture.width, 180f / 1536f),
                    "Goblin uses measured source columns, not equal eighths or ninths");
                Require(goblin.Frame(UnitSpriteSheet.Pose.Idle, 1.81f) == first, "Goblin idle loops after nine frames");
            }
            PixelPresentationChecks.Run();
            UnitMovementChecks.Run();
            CombatChecks.Run();
            AbilityChecks.Run();
            VisibilityChecks.Run();
            Debug.Log("Generated unit sprite checks passed: hero V2 atlases, preserved enemy art, frame timing, movement, combat, abilities and visibility.");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Unit sprites: " + message); }
    }
}
