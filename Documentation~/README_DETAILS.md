# NonsensicalKit.DigitalTwin 详细介绍

`com.nonsensicallab.nonsensicalkit.digitaltwin` 是数字孪生能力包，提供数据接入、设备运动控制、机械联动、渲染映射与工业协议接入（MQTT/Socket.IO）等能力。

---

## 核心模块一览

### Motion.DataHub

- **模块定位**：统一接收点位数据并按部件聚合后分发。
- **核心入口**：`DataHub`、`PointData`、`PartConfig`
- **使用方法**：
  1. 在配置中定义部件与点位关系（`PartConfig`）。
  2. 运行时向 `DataHub` 发布单点或批量点位数据。
  3. 由 `DataHub` 聚合后广播 `MotionPartUpdate`，驱动各部件更新。

### Motion.DirectControl

- **模块定位**：将聚合后的部件点位直接映射为动画/位移/旋转行为。
- **核心入口**：`PartMotionBase`、`RotatePartMotion`、`CylinderPartMotion`、`GripperPartMotion`
- **使用方法**：
  1. 在部件对象挂载 `PartMotionBase` 派生类。
  2. 配置 `m_partID` 与点位定义保持一致。
  3. 在派生类 `OnReceiveData(...)` 中实现具体运动逻辑。

### Motion.MechanicalDrive

- **模块定位**：构建“动力源 -> 机构”联动链路，模拟真实机械传动。
- **核心入口**：`Engine`、`Mechanism`、`Gear`、`Belt`、`Chain`
- **使用方法**：
  1. 在场景中搭建引擎与机构层级关系。
  2. 通过 `Drive(...)` 向链路输入功率与驱动类型。
  3. 使用对应 Editor 工具可视化调整锚点与传动参数。

### MQTT

- **模块定位**：从 MQTT Broker 订阅设备数据并转发到业务通道。
- **核心入口**：`MqttService`、`MqttManager`、`MqttClientConfig`
- **使用方法**：
  1. 在配置中维护连接参数、Topic 前后缀和认证信息。
  2. 启动后由 `MqttService` 自动创建并运行一个或多个 `MqttManager`。
  3. 将订阅到的数据转换为点位消息后接入 `DataHub`。

### SocketIO

- **模块定位**：接入 Socket.IO 数据源并统一转换为点位流。
- **核心入口**：`SocketIOManager`、`SocketIOToPointData`
- **使用方法**：
  1. 配置 Socket 通道 key 与消息格式约定。
  2. 在 `SocketIOToPointData` 中将 JSON 消息反序列化为 `PointData`。
  3. 通过发布 `receivePoints` 事件驱动后续运动系统。

### Render

- **模块定位**：基于业务数据驱动货架/货物的批量渲染映射。
- **核心入口**：`ShelvesCargoRender`、`RenderConfig`、`MultiRender`
- **使用方法**：
  1. 初始化货架映射数据并调用 `InitShelvesCargo`。
  2. 通过 `WriteShelvesCargo` 写入变更，更新货位显示状态。
  3. 结合 `CargoType` 与预制体配置完成批量显示与刷新。

### Items

- **模块定位**：提供场景搭建阶段常用的辅助组件与演示能力。
- **核心入口**：`DataPlayer`、`FlyCamera`、`VisualCameraSwitcher`
- **使用方法**：
  1. 用 `DataPlayer` 回放历史点位数据进行离线验证。
  2. 用 `FlyCamera` 进行自由视角巡检。
  3. 用 `VisualCameraSwitcher` 在多机位间切换观察状态。
