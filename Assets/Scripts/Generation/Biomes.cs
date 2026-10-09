using System;
using GuildTactics.HexGrid;

namespace GuildTactics.Generation
{
    // Crypt stays zero so older serialized configurations retain their original biome.
    public enum DungeonBiome { Crypt, FloodedCellar }

    public static class Biomes
    {
        public static void Validate(DungeonBiome biome)
        {
            if (!Enum.IsDefined(typeof(DungeonBiome), biome)) throw new ArgumentException("Unknown dungeon biome.");
        }
        public static string Name(DungeonBiome biome) => biome == DungeonBiome.FloodedCellar ? "Flooded cellars" : "Crypt ruins";
        public static string TerrainName(DungeonBiome biome, TerrainType terrain) => biome == DungeonBiome.FloodedCellar &&
            terrain == TerrainType.Pit ? "Deep water (fatal if pushed)" : terrain.ToString();
    }
}
