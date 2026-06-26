namespace NonsensicalKit.DigitalTwin.Motion
{
    public enum EngineState
    {
        /// <summary>
        /// 静止
        /// </summary>
        Stationary = 0,

        /// <summary>
        /// 减速
        /// </summary>
        Decelerating,

        /// <summary>
        /// 加速
        /// </summary>
        Accelerating,

        /// <summary>
        /// 全速
        /// </summary>
        FullSpeed
    }
}
