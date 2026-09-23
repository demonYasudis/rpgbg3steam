using System;

namespace GuildTactics.Generation
{
    [Serializable]
    public sealed class DungeonGenerationConfig
    {
        public const int MinimumEnemyCandidates = 8;
        public int RoomCount = 4;
        public int RoomRadius = 2;
        public int HighGroundPercent = 12;
        public int PitPercent = 10;
        public int MaxAttempts = 4;
        public int MinimumWalkableCells = 45;
        public int EnemyDistanceFromParty = 4;

        public void Validate()
        {
            if (RoomCount < 3 || RoomCount > 5 || RoomRadius < 1 || RoomRadius > 3 ||
                HighGroundPercent < 0 || HighGroundPercent > 100 || PitPercent < 0 || PitPercent > 100 ||
                MaxAttempts < 1 || MaxAttempts > 20 || MinimumWalkableCells < 16 || MinimumWalkableCells > 100 ||
                EnemyDistanceFromParty < 2 || EnemyDistanceFromParty > 6)
                throw new ArgumentException("Invalid dungeon configuration.");
        }
    }
}
