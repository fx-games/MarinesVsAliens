namespace DefaultNamespace
{
    public static class GameController
    {
        public enum GameMode
        {
            None,
            Campaign,
            Skirmish,
            WatchAI
        }

        public static GameMode ChosenMode { get; set; } = GameMode.None;
        public static int[] SkirmishFactions { get; set; } = new int[]{0,1,2,3};
        public static int CampaignFaction { get; set; } = 0;
        public static int SkirmishPlayerCount { get; set; } = 2;
        public static int CurrentMapSeed { get; set; }
    }
}