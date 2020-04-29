namespace DefaultNamespace
{
    public static class GameController
    {
        public enum GameMode
        {
            None,
            HotSeat,
            AgainstAI,
            WatchAI
        }

        public static GameMode ChosenMode { get; set; } = GameMode.HotSeat;
        public static int[] SkirmishFactions { get; set; } = new int[]{0,0,0,0};
    }
}