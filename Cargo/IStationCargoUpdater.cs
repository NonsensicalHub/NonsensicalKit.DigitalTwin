namespace NonsensicalKit.DigitalTwin.Cargo
{

/// <summary>
/// 货物控制器帧更新接口，供 DigitalTwinBootstrap 统一驱动。
/// </summary>
public interface IStationCargoUpdater
{
    /// <summary>驱动所有活跃货物的移动插值，每帧调用一次。</summary>
    void UpdatePos();
}

/// <summary>StationCargoController 的工位 key 类型，决定 IOCC 注册时的泛型参数。</summary>
public enum StationCargoKeyType
{
    String,
    Int,
}
}
