namespace GuildTactics.HexGrid
{
    /// <summary>Shared traversal rules. High ground has no combat bonus in this prototype.</summary>
    public static class TerrainRules
    {
        public static bool CanWalk(TerrainType terrain) =>
            terrain == TerrainType.Ground || terrain == TerrainType.HighGround;

        public static bool CanPushInto(TerrainType terrain) => CanWalk(terrain) || terrain == TerrainType.Pit;
    }
}
