namespace NonsensicalKit.DigitalTwin.Motion
{
    public enum Axis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    public enum AxisDir
    {
        X = 0,
        Y = 1,
        Z = 2,
        IX = 3,
        IY = 4,
        IZ = 5,
    }

    public enum AxisSelect
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 4,
        XY = 3,
        XZ = 5,
        YZ = 6,
        All = 7,
    }
}
