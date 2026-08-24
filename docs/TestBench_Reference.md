# TestBench_Reference.md — 老平台迁移权威参考文档

**生成时间**：2026-08-23
**项目路径**：`E:\WPFCli\References\OldPlatform\NewTestBench\Bots.TestBench`
**文档定位**：后续平台迁移（重构、技术栈升级、功能复现、接口对接）的唯一权威参考。

---

## 1. 项目整体概览

| 属性 | 说明 |
|---|---|
| 技术框架 | .NET Framework 4.8 + WPF + MahApps.Metro |
| 解决方案 | `Bots.TestBench.sln`（100+ 个 `.csproj`，按数字前缀分类） |
| 核心架构 | MVVM（Caliburn.Micro 辅助）+ EF6 + 自研通信抽象（Xmas11.Comm） |
| 数据策略 | SQLite 本地库（离线韧性）+ SQL Server 远程库（CSTDATA/U8/MMS）+ 双库同步/重试 |
| 通信协议 | 串口、TCP、USB（VID/PID）、USB HID、蓝牙 BLE、Modbus（485） |
| 核心业务 | JSON 驱动自动测试任务 + 设备驱动（DUT/Tool/Aging/Upgrade/BarCode）+ 数据同步 + 固件升级（LSD 协议）+ SOAP 外部系统集成 |

---

## 2. 解决方案与模块结构

### 2.1 数字前缀分类

| 前缀 | 含义 | 典型内容 |
|---|---|---|
| `01-Common` | 通用工具、扩展方法 | 字符串处理、配置读取 |
| `02-Framework` | 框架：MVVM 助手、通信抽象、Pub/Sub 消息、脚本引擎、任务调度、主题管理 | `Framework.*` |
| `03-DataBase` / `11.APP\DataBase` | 数据访问层、DBContext、DAO 类 | `DBContext`、`DBModels`、`DataClass` |
| `04-Model` | 领域模型：Task、DUT、数据实体映射 | `Task/TestTask`、`DUT` |
| `05-Device` | 设备驱动与通信抽象 | `Device.Base`、`Device.DUT`、`Device.Tool`、`Device.BarCode`、`Device.Upgrade`、`Device.Aging` |
| `07-Tools` | 外部工具集成 | 条码解析、文件处理、固件解析 |
| `11-APP` | 主入口、UI 层、业务管理、任务引擎、数据库初始化、服务集成 | `App`、`Business`、`Services`、`DataBase` |
| `13-UnitTest` | 单元测试工程 | — |
| `14-Documents` | 业务文档、方案、流程图、指令集 | `.xmind`、`.md`、`.pptx`、`.xlsx` |

---

## 3. 核心技术栈与架构模式

- **MVVM 模式**：视图 `.xaml`（MahApps.Metro `MetroWindow`、`Tile`、`Expander`、`Menu`、`StatusBar`）；视图模型继承 `SimpleViewModel`（自研 INPC，`SetPropertyValue` 触发通知）；模型包含 `TestTask`、`DUT`、`CommConfig`、`UpgradeFile`、DB 实体。
- **组合模式（任务结构）**：`TestTask` → `TaskCollection`（`List<ISubItem>`）；`ISubItem` 可为 `TestGroup`（支持嵌套）或 `AutoTestItem`/`ManualTestItem`。
- **脚本引擎**：`ScriptExecutor` 支持运行时编译执行 C# 脚本（`RefAssemblies` 指定外部程序集引用路径）。
- **Pub/Sub 消息模式**：设备状态、任务进度、UI 事件的跨模块解耦通信（`02-Framework` 层实现）。
- **任务配置**：JSON 驱动；`Tasks.json` 定义产品组与 TaskProfile；`Profile` 属性指向具体任务配置文件。
- **数据访问**：双库策略（SQLite 本地 + SQL Server 远程）；DBContext 工厂方法 `CreateLocalInstance()` / `CreateRemoteInstance()` / `CreateU8Instance()` / `CreateMMSInstance()`；`EnsureLocalDatabaseSchema()` 自动修复本地结构。

---

## 4. 应用入口与主窗口

### 4.1 Program.cs 启动流程

路径：`11.APP\Bots.TestBench.App\Program.cs`

1. 单实例互斥（命名 Mutex）
2. 自动启动注册（注册表 Run 键 → `AutoUpgrading.exe`）
3. 国际化设置（Culture=zh / en-US）
4. 登录窗口 → 主窗口
5. 全局异常捕获（UnhandledException / DispatcherUnhandledException）

### 4.2 App.cs

- 加载 Metro 样式：BaseLight 主题 + Blue 强调色
- 初始化 UserManager、DBContext 映射视图

### 4.3 App.config 关键配置

- 文化/开关：`Culture=zh`、`LoginEdition=Service`、`IsUseRemoteConn=true`
- 缓存路径：`LocalCachePath=FirmwareProgram\`
- AEM 集成：`AEM_ApiKey`、`AEM_BaseUrl`
- 连接字符串：`localConn`(SQLite)、`remoteConn`(SQL Server CSTDATA)、`testRemoteConn`、`U8Conn`(UFDATA_555_2014)、`MMSConn`
- SOAP 端点：`CSTDBService.asmx`(192.168.0.191:9040)、`DBServices.svc`(192.168.0.131:8001)、`DispachService.asmx`(bh.const.cc)
- 远程服务账号：`http://192.168.0.134:10001/`（softadmin / ConST12345）
- EF Provider：SQLite EF6 provider

### 4.4 MainWindow.xaml / MainWindow.xaml.cs

布局：
- 根元素：`ctls:MetroWindow`
- 菜单：配置（BarcodeScannerConfigCommand、HomeConfigCommand、ReturnToRetroCommand、NewUICommand）；帮助（OperationExpressShowCommand、AboutShowCommand）；退出（CloseCommand）
- 状态栏：AppTitle、BarcodeStatus、OnlineStatus
- 主内容区：列 0 任务组 ItemsControl + WrapPanel(306)；列 1 可折叠 TrialDataChartControl
- Tile 点击通过 OpenTaskCommand 打开任务；样式由 TileStyleKeyToResourceConverter 动态绑定
- 触发器：Loaded → LoadedCommand；Closing → ClosingCommand

搜索过滤逻辑：
```csharp
// 首次搜索缓存初始状态（ShowExpandClass/ShowClass 记录 isShow/isExpanded）
if (searchText.Length > 0) {
    // 默认隐藏所有组；匹配 Profile 显示且父组 IsShow=true、IsExpanded=true
} else {
    // 从缓存恢复原始可见性与展开状态
}
```

### 4.5 Tasks.json

路径：`11.APP\Bots.TestBench.App\Task\Tasks.json`

顶层为产品组数组（ConST650/660/670/680/685、CC30、CDP、DPS、DPSEX、P21~P28、T01~T05 等），每项含：

```json
{
  "SortIdentifier": 1,
  "Key": "...",
  "Name": "标准测试",
  "IsShow": true,
  "Background": "...",
  "Profile": "TaskProfiles\\ConST670\\Standard.json"
}
```

---

## 5. 核心领域模型 TestTask

路径：`04.Model\Bots.TestBench.Model.Task\TestTask\TestTask.cs`

- 继承 SimpleViewModel
- 组合模式：TestTask 包含 `TaskCollection: List<ISubItem>`（可为 TestGroup/AutoTestItem/ManualTestItem）
- `DevicesDic: Dictionary<string, BaseDevice>`；DUT 引用被检设备
- ScriptExecutor：动态 C# 脚本执行引擎

关键方法：

```csharp
public bool Run() {
    this.Devices.ForEach(d => { if (!d.IsOpen) d.Open(); isAllOpen &= d.IsOpen; });
    if (!isAllOpen) return false;
    this.BeginTime = DateTime.Now;
    this.TaskCollection.ForEach(t => t.Run());
    this.EndTime = DateTime.Now;
    return true;
}

public virtual Result GetResult() {
    // 聚合 WaitTestTaskCollection 各子项 Assert 的逻辑与；ErrMsgs 汇总
}

public static TestTask LoadTask(string profileFile) {
    // JSON 反序列化 + 初始化 ScriptExecutor + LoadWaitTestTask()
}
```

序列化：JSON（Newtonsoft.Json 自定义 settings）、XML（SerializeXml）

---

## 6. 业务层 TestTaskManager

路径：`11.APP\Business\Bots.TestBench.Business\TestTaskManager.cs`

核心机制：
- 任务执行队列：`static Queue<Task> execTaskQueue`（容量5）+ `currTask` + ContinueWith + lock(objlock)

```csharp
static void ExecTask(Task task) {
    lock (objlock) {
        if (currTask != task) {
            if (currTask == null) {
                currTask = task;
                currTask.ContinueWith((t) => ExecTask(t));
                currTask.Start();
            } else execTaskQueue.Enqueue(task);
        } else {
            if (execTaskQueue.Count > 0) {
                currTask = execTaskQueue.Dequeue();
                currTask.ContinueWith((t) => ExecTask(t));
                currTask.Start();
            } else { currTask?.Dispose(); currTask = null; }
        }
    }
}
```

- 本地优先 + 远程回退：先写 SQLite（AddToLocal）→ 再写远程（AddToRemote_new/old）→ 失败加入 Bots_TaskUploadPending 待补传队列
- 任务身份解析：IsSameTaskIdentity（验证设备+类别）、ResolveTaskDataForSave（复用 TaskGUID 或新建）
- 外部系统同步：
  - 出厂检验（TaskType=="0"）：DBService.UpdateCheckResult() 同步发货系统
  - 非出厂（!="0" 且 !="14"）：DeviceManager.SetSelftCheckTime()
  - 失败写入 Bots_ExternalSyncPendingDAO.AddOrUpdate()，SubmitExternalSyncPendings() 重试

完整保存流程（SaveTestTask）：
1. 本地保存任务主表（AddTaskToLocal）
2. 本地保存子项数据（AddToLocal）
3. 远程保存任务主表（AddTaskToRemote_new/old）
4. 远程保存子项数据
5. 远程成功 → RemoveLocalSavedTask 清理本地
6. 外部系统同步（HandleExternalSyncAfterTaskSaved）
7. 更新上传待办状态（MarkUploading → MarkRemoteFailed / Remove）

---

## 7. 数据库与数据访问层

### 7.1 实体类

路径：`11.APP\DataBase\Bots.TestBench.DBModels\DBEntity\`

| 类名 | 关键字段 |
|---|---|
| Bots_TaskData | TaskGUID(PK)、TaskName、TaskType、TaskCategories、BatchCode、DeviceCode、DeviceMode、DeviceSalesType、DeviceCategory1-5、BeginTime、EndTime、IsPass、IsFirstPass、IsAll、NoPassTag、NoPassInfo、ByUser、DevicesInfo、Report、Remark、CreateTime、UpdateTime、backup1/2/3 |
| Bots_SubItemData | SubItemGUID(PK)、TaskGUID、ParentGUID、SubItemIndex、SubItemName、BeginTime、EndTime、IsPass、InterruptStatus(TINYINT)、NoPassTag、NoPassInfo、Data、ByUser、Remark、CreateTime、UpdateTime、backup1/2/3 |
| Bots_TestConclusions | GUID(PK)、TaskName、TaskType、TaskCategories、SubItemName、Conclusion、CreateTime、UpdateTime |
| Bots_ExternalSyncPending | GUID(PK)、TaskGUID、TaskName、TaskType、TaskCategories、DeviceCode、IsPass、IsAll、EndTime、ByUser、SyncType、RetryCount、LastError、CreateTime、UpdateTime |
| Bots_TaskUploadPending | GUID(PK)、TaskGUID、TaskName、TaskType、TaskCategories、DeviceCode、Status(Uploading/RemoteFailed/CleanupPending)、RetryCount、LastError、LastHeartbeatTime、RemoteSavedTime、CleanupTime |
| Bots_RetryReasonRecord | RecordGUID(PK)、TaskGUID、SubItemGUID、PrevTaskGUID、PrevSubItemGUID、RetryCategoryCode(A/B/C/D)、RetryCategoryName、IsProductIssue、RetryIndex、PrevNoPassTag、PrevNoPassInfo、PrevTestTime、OperateBy、OperateTime |

其他注册实体：Bots_RepairRecord/Details/Categoriy、MeterERP_*、Bots_User、Bots_Role

### 7.2 DAO 层

路径：`11.APP\DataBase\Bots.TestBench.DataAccess\DataClass\`

| DAO | 关键方法 |
|---|---|
| Bots_TaskDataDAO | AddToRemote_new/old、AddTaskToRemote_new/old、AddToLocal/AddTaskToLocal、SubmitTestTaskAndItems/Core、CheckAgingTaskRemoteIsAllCorrectness（老化完整性）、CheckNormalTaskRemoteIsAllCorrectness、GetRemote/GetLocal、GetRemoteLast/GetLocalLast、GetDatasFromRemote（分页）、LocalRemove、RemoteIsExist 系列 |
| Bots_SubItemDataDAO | AddToRemote_new/old(List)、AddToLocal、GetLocalSubItemDatas/Data、GetRemoteSubItemData、GetTaskSubItemFirst/LastData、LocalRemove |
| Bots_TestConclusionsDAO | Add/AddToLocal、GetLocalTestConclusions、LocalRemove、GetConclusions |
| Bots_ExternalSyncPendingDAO | AddOrUpdate、GetLocalPendings、LocalRemove、MarkFailed |
| Bots_TaskUploadPendingDAO | MarkUploading、MarkRemoteFailed(IfLocalTaskExists)、MarkCleanupPending、Remove、GetEffectivePendings、GetCleanupPendings、RecoverUploadPendings（超时恢复：Uploading>6min→RemoteFailed）、CheckRemoteUploadCompleted |
| Bots_RetryReasonRecordDAO | AddToRemote/AddToLocal、GetLocalList、DeleteLocal、GetLastSubItemResults（批量优化排除Data字段）、GetRetryCount |

### 7.3 DBContext

路径：`11.APP\DataBase\Bots.TestBench.DataAccess\DBContext.cs`

- 工厂方法：CreateLocalInstance / CreateRemoteInstance / CreateU8Instance / CreateMMSInstance
- EnsureLocalDatabaseSchema()：自动修复 SQLite 结构（如添加 Bots_SubItemData.InterruptStatus TINYINT 列；确保 TestConclusions、ExternalSyncPending、TaskUploadPending、RetryReasonRecord 表存在）

---

## 8. 设备层（05-Device/Base）

### 8.1 枚举定义

```csharp
public enum DeviceType { DUT, STD, Tool, AgingPositon }
public enum ConnectStatus { DisConnected, DisConnectting, Connectting, Connected, Error }
public enum CommType { None=0, SeriialPort=1, Ethernet=2, WLAN=3, USB=4, HID=5 }
```

### 8.2 BaseDevice

```csharp
public abstract class BaseDevice : SimpleViewModel, IDisposable {
    public bool HasConnected { get; set; }
    public string DeviceKey { get; set; }
    public string DeviceMode { get; set; }
    public DeviceType DeviceType { get; set; }
    public CommConfig CommConfig { get; set; }
    public ObservableCollection<CommConfig> CommConfigs { get; set; }
    public Xmas11.Comm.Devices.BaseDevice CommInstance { get; set; }
    public ConnectStatus ConnectStatus { get; set; }

    public abstract bool Open();
    public virtual void Initialization() { ReadCommConfigFromINI(); }
    public bool FirstManualAfterAutoConnectSpDevice<T>(...);   // 手动优先→自动连接+重试
    public bool AutoConnectUsbDevice<T>(...);
    public void Dispose();
}
```

连接策略：串口/USB/HID 支持"首次手动确认 → 之后自动连接"，带重试与存在性验证。

### 8.3 CommConfig 家族

```csharp
[XmlInclude(typeof(EthernetConfig))]
[XmlInclude(typeof(SerialPortConfig))]
[XmlInclude(typeof(USBConfig))]
public class CommConfig : SimpleViewModel, ICloneable {
    public string Name { get; set; }
    public string SerialNumber { get; set; }
    public bool IsSave { get; set; } = true;
    public string DevSn { get; set; }
    public string ComName { get; set; }
    public virtual string GetAddress() => "";
    public virtual bool SetAddress(string address) => true;
    public virtual Xmas11.Comm.Core.CommSettings GetCommSettings() => new SocketCommSettings();
}
```

| 派生类 | 关键属性 |
|---|---|
| SerialPortConfig | SPName(COM口)、Bauds、DataBits、StopBits、Parity |
| EthernetConfig | IP、Port、StartIp/EndIp（IP范围扫描 GetIPRange()）、DS_IP |
| USBConfig | VID、PID、LocationPath、SerialNumber |

### 8.4 DUT 实体（被检设备信息）

路径：`05.Device\Base\DUT.cs`

- Info 字典 + FieldNameMap 中英映射（DeviceName→设备名称、BatchCode→批次号、DeviceRange→量程、Upper→量程上限、Lower→量程下限、Unit→单位、Accuracy→精度、DeviceCategory1→销售渠道 等）
- 基本信息：DeviceName、BatchCode、DeviceCode、DeviceMode、DeviceSalesType、DeviceRange、DetailType
- 量程信息：Upper、Lower、Unit、Accuracy；多量程：MultiRanges(List&lt;RangeInfo&gt;)、HasMultiRanges
- RangeInfo：MeaSure / Unit / Accuracy

### 8.5 AgingPosition（老化工位）

继承 BaseDevice。核心成员：DeviceSN、IsOnline/IsError/IsOK、RealTimeMsg(Detail)、dynamic AgingDevice。

定时扫描与自动重连：

```csharp
private void timer_Timer(object sender, EventArgs e) {
    if (AgingDeviceIsOnline) {
        if (CheckDevice()) { RealTimeMsg = "通讯正常，信息匹配"; IsError = false; RefreshBaseData(); }
        else { RealTimeMsg = "通讯异常"; IsError = true; ScanDeivce(); }
        RefreshData();
    } else ScanDeivce();
}

public bool ScanDeivce() {
    if (TryConnecteCount <= 0) return false;
    ConnectStatus = ConnectStatus.Connectting;
    if (this.Open()) { ConnectStatus = ConnectStatus.Connected; TryConnecteCount = 0; return true; }
    TryConnecteCount--;
    ConnectStatus = ConnectStatus.DisConnected;
    return false;
}
```

其他方法：Online()、SetConnect(int tryCount)、StartScan()/StopScan()、RefreshBaseData()/RefreshData()

### 8.6 CommInstanceFactory（通信实例工厂/连接池）

```csharp
public class CommConfigTab {
    public Semaphore Semaphore { get; }
    public CommType CommType { get; set; }
    public CommConfig CommConfig { get; set; }
    public string SerialNumber { get; set; }
    public bool IsAssigned { get; set; }
    public bool IsConnected { get; }
    public bool IsExist { get; }
    public Xmas11.Comm.Devices.BaseDevice CommDevice { get; set; }
}
```

- ConcurrentDictionary&lt;CommType, List&lt;CommConfigTab&gt;&gt; 连接池；信号量控制并发
- GetCommInstance(CommConfig) / ReleaseCommInstance(commDevice)；空闲连接自动清理

其他基础组件：BluetoothDeviceManager（SetupAPI 枚举/启停蓝牙设备）、USBSetupApiHelper（USB 枚举）、IResponseDealer（响应处理）

---

## 9. 设备驱动模块（DUT）

### 9.1 设备目录清单

路径：`05.Device\DUT\`

| 目录 | 主类 | 附加文件 |
|---|---|---|
| ConST125 | ConST125 | — |
| CC30 | ConSTCC30 | Resources 图片 |
| CDP | CDPBase / CDPH / CDPR / IPM | LSD 升级协议实现 |
| ConST171A | P27CommonBase | 共享基类 |
| ConST211A | ConST211A | — |
| ConST211H | ConST211H | Resources |
| ConST218 | ConST218 | ConST218_WireLess 无线扩展 |
| ConST221 | ConST221 | ConST218AP、ConSTDPG_Base、IdentifyType |
| ConST283 | ConST283 | — |
| ConST28X | ConST289 | T4PCommonBase 共享基类 |
| ConST31X | ConST31X（基类） | ConST310、ConST312 |
| ConST320EX | ConST320X | Data/ConST326EXErrorCodesMap 错误码映射 |
| ConST326 | ConST326、ConST602 | Data/ConST326ErrorCodesMap |
| ConST380 | ConST380 | DataStruct/EMMTestProject |
| ConST560 | ConST560 | ModelConverter |
| ConST575 | ConST575 | — |
| ConST610 | ConST610 | — |
| ConST630 | ConST630 | ConST630TBase、ConST630TSeries |
| ConST650 | ConST650 | Resources 图片 |
| ConST660 | ConST660 | — |
| ConST670 | ConST670、ConST1210 | Resources |
| ConST680 | ConST680 | — |
| ConST683A | ConST683A | — |
| ConST685 | ConST685、ConST685_EMM、ConST685_TML | DataStruct/EMMTestProject |
| ConST801 | ConST801 | — |
| ConST810 | ConST810 | — |
| ConST811 | ConST811 | — |
| ConST811A | ConST811A | ConST811A_CMM、ConST811A_Tool |
| ConST82X | ConST82X、ConST836HP_CB | Resources |
| ConST860 | ConST860 | ConST860ForController、ConST860_ControlValve |
| DPS | DPSBase、DPS100、DPSHP | — |
| DPSEX | DPSEX | Resources |
| ConST630A1/R1 | ConST630A1 / ConST630R1 | — |

通用模式：
- 继承 BaseDevice 或 UpgradeDevice
- 实现 Open() 具体协议（串口参数、USB VID/PID、以太网 IP/Port、HID）
- 提供设备指令集（GetInfo/SetParameter/ReadData 等）
- 部分含错误码映射表（*ErrorCodesMap.cs）与自定义数据结构（EMMTestProject.cs）

### 9.2 代表驱动：CDPBase（LSD 引导加载协议）

路径：`05.Device\DUT\Bots.TestBench.Device.CDP\CDPBase.cs`，继承 UpgradeDevice。

核心方法：
- Open()：创建 Xmas11.Comm.Devices.CDPBase 通信实例
- LSDReset()：发送引导加载复位命令（0x88 0x01 0x55...）
- LSDHandshake()：等待设备响应，40 秒内检查返回字符串包含 "UPDATE"
- LSDEraseChip()：擦除闪存，期望响应 0x10 0x30
- LSDWriteFlash(uint address, byte[] data)：62 字节分包写入，XOR 校验
- LSDLoadFirmware(UpgradeFile)：解析 @ 分隔的固件文本文件，逐行提取地址/数据/校验并写入
- LSDRunApp()：发送应用启动指令

完整升级流程：LSDReset → LSDHandshake → LSDEraseChip → 循环 LSDWriteFlash → LSDRunApp

---

## 10. 工装设备模块（Tool）

路径：`05.Device\Tool\`，共 40+ 子目录。

分类：

| 类别 | 工装 | 通信 |
|---|---|---|
| 压力/流量控制 | ConST125TestTool、ConST218Z_STestTool、ConST283TestTool、ConST630TAtmoBoardTestTool、ConST630TMoistBoardTestTool、ConST630TSeriesTestTool | 串口/Modbus(485) |
| 泵类 | P21PumpTestTool（串口/网络）、P25_PumpTestTool（网络） | 串口/TCP |
| 开关/阀门 | P26_Switch、P26_SwitchHC、GeneralZQWL(ZQWLDevice, 485/Modbus)、P22RelayTool | 串口/485 |
| 模拟量采集/传感器 | ZQWL_AIRead(MN5802D, Modbus)、T04T15SensorBoardTestTool、UD18TestTool、WCS2702TestTool | 485/串口 |
| 特殊功能 | DynamicStandardTestBench(DSTB, 网络/串口)、Win10BLEDevice(BLE)、PortableStoveTestTool、ZM32Tool | 网络/BLE/串口 |

技术特点：自动扫描可用串口、VID/PID 识别 USB 设备、异步数据接收、事件驱动、超时重试、日志记录。

---

## 11. 条码扫描模块（BarCode）

### 11.1 接口与数据结构

IScanner 接口（`05.Device\Base\BarCode\IScanner.cs`）：IsExist、AutoSearchScanner、DeviceAddress/CanWriteAddress/SetDeviceAddress、DeviceCommInstance、CommSettings、Online/Enabled、Open/OnBeforeClose/Close、RegisterAsyncReceivedHandler/UnRegisiterAsyncReceivedHandler、OnInstrumentComm 事件

BarCodeData：TypeID、SubTypeID、Data

ScannerType 枚举（`BarCodeHelper\ScannerType.cs`）：MS1690、Xenon1900、Xenon1902、USBKeyboard、UnEnable、Unknown

### 11.2 实现

| 实现 | 通信 | 参数/特性 |
|---|---|---|
| Xenon1900 | 串口(Xmas11.Comm.Devices.Xenon1900) | 默认 115200/8/1/None；自动搜口；异步接收；"/"分隔多字段解析 |
| Xenon1902 | 串口 | 默认 9600；多线程并行搜口（FindUsablePort + WaitHandle[]） |
| USBKeyboard | USB HID(HIDKeyboard) | VID=3118 PID=2337；Read() 读缓冲转 ASCII，去 \r\n |
| BarcodeScannerManager | 管理层 | Scan()/Clear()/ScannerReceived 统一分发 |

---

## 12. 固件升级模块（Upgrade）

路径：`05.Device\Upgrade\`

### 12.1 IUpgradable 接口

- 控制：RequestStopUpgrade、IsUpgrading
- 检查：IsUpgradable()、UpgradeCheck()、UpgradeFileCheck()、CheckUpgradeFile()
- 文件/配置：LoadUpgradeFile()、LoadUpgradeSetting(path)/SetUpgradeSetting/GetUpgradeSetting
- 信息：SetUpgradeInfo/GetUpgradeInfo、RefreshMainInformation/RefreshCurrentVersion/RefreshUpdateVersion/InitializationMainInformation
- 操作：Upgrade()、StopUpgrade()
- 日志：SaveUpgradedLog(DateTime)、SaveInUpgradingLog(DateTime)

### 12.2 UpgradeDevice 基类

属性：RequestStopUpgrade、IsUpgrading、UpgradeSetting、UpgradeInfo、DeveiceSN（注意拼写）

升级流程骨架：

```csharp
public virtual UpgradeInfo Upgrade() {
    IsUpgrading = true;
    UpgradeInfo.IsProgress = true;
    UpgradeInfo.ProgressIsIndeterminate = true;
    try {
        if (0 == UpgradeCheck()) { /* 执行具体协议升级 */ }
        if (RequestStopUpgrade) return UpgradeInfo;
        return UpgradeInfo;
    } finally {
        ProgressIsIndeterminate = false;
        IsProgress = false;
        IsUpgrading = false;
    }
}
```

### 12.3 配套类

| 类 | 关键成员 |
|---|---|
| UpgradeSetting | FileSettingPath、Name、DeviceSN、IsLocal、RemoteServiceEnable、UpgradeFiles(ObservableCollection)，XML 序列化 |
| UpgradeInfo | MainInfoDic/MainInfoList、VersionInfoList、UpgradeMsgs、IsProgress、ProgressIsIndeterminate |
| UpgradeFile | IsMain、IsSelectEnable、IsUpdateEnable、IsLocal、FileName、LocalFilePath、RemoteFilePath、CachePath、IsCached、IsAnalyzed |
| VersionInfo | Key、Name、CurrentVersion、UpgradeVersion、UpgradeVersionList |
| UpgradeMsg | Name、Content |
| UpgradeParameter | TextParameter/ValueParameter(带单位)/BoolParameter 及对应 Collection 类型 |

升级流程：加载配置 → 文件检查 → 设备检查 → 版本对比 → 执行协议升级 → 记录日志（进入/完成）

---

## 13. 外部服务集成（SOAP）

路径：`11.APP\Services\Bots.Service.ServiceHelper\Service References\`

### 13.1 SendItemSystemService（DispachService.asmx @ bh.const.cc）

方法清单：SendMateOrder、SendTransPro、StockMateOrder、StockTransPro、DeleteSn、SetCertFinished、IsFirstHost、GetAddressInfo、GetSendType、UpdateAddressInfo、ChangeDispatchOrderCrm、ChangeDispatchOrder、EditBorrowOrder、ChangeStockOrderCrm、ChangeStockOrder、PushReturnOrder、PushGiveBackOrder、PushBorrowOrder、PushBorrowGiveBack、SetBorrowSendCount、ChangeBorrowOrder、CreateTransition、GetProductStatusViewList、UpdateCheckResultNew、UpdateCheckResult、GetDevicesSpecificationBySN、GetDevicesSpecificationBySN_new

用途：库存/调拨/发货管理、测试结果同步（UpdateCheckResult 由 TestTaskManager 出厂检验调用）、设备规格查询

### 13.2 DipperDBService（CSTDBService.asmx @ 192.168.0.191:9040）

方法：UpdateJLYSTDInfo(jsonSTDStr, stdID)、UpdateBotsTestData(jsonTestData)、CreateBotsTestData(jsonTestData)、ReadInfoByCode(deviceCode)、GetLatestAtoms(返回 double)

用途：测试数据上传（JSON 序列化的任务/子项数据）、计量院标准信息更新、设备编码查询、原子值获取

### 13.3 MeterERPDBService（DBServices.svc @ 192.168.0.131:8001）

复杂数据类型：PressureRange、Unit、EquivalentAlgorithm 等；与 U8/MeterERP 数据交互（配合 U8Conn 连接字符串使用），具体操作方法见完整 Reference.cs

调用模式：直接实例化代理客户端或配置端点绑定；同步调用为主（部分提供 Begin-End 异步版本）；内部封装超时（约30s）、重试（约3次/间隔1s）、异常捕获（CommunicationException/TimeoutException/FaultException 转 bool 或自定义结果）

---

## 14. 数据防丢失与补传机制（迁移关键）

参考文档：`11.APP\测试平台相关文档\03.测试任务结果保存防丢失记录.md`

状态机（Bots_TaskUploadPending.Status）：

```
[新建] --MarkUploading--> Uploading
Uploading --成功+远程确认--> [删除]（CheckRemoteUploadCompleted 校验远程完整性后清理）
Uploading --超时>6分钟(RecoverUploadPendings)--> RemoteFailed
RemoteFailed --重试成功--> CleanupPending --> [删除]
```

要点：
- RecoverUploadPendings：启动时扫描，将超时 Uploading 转 RemoteFailed，孤儿记录清理
- MarkRemoteFailedIfLocalTaskExists：仅当本地任务存在时标记失败（防止误标）
- 外部同步独立于任务上传：Bots_ExternalSyncPending 单独跟踪 MES/发货系统同步，互不影响

---

## 15. 现有文档资源（14-Documents）

路径：`14.Documents\`

| 目录/文件 | 内容 |
|---|---|
| 根目录 xmind | 固件升级测试进度、工作总结-李康康、测试平台代码结构、各月份计划 |
| ConST125\ | 整机测试方案、连接台测试工装 PLC 寄存器说明、822 连接台串口指令表、跟踪记录单 |
| ConST660\ | 产品介绍、指令集、控制板/测量板串口指令、组件及整机下线测试步骤、产品参数配置表、跟踪记录单 |
| ConST670\ | 1210 炉指令集、计量炉指令集、产品参数配置表、跟踪记录单 |
| ConST82X\ | 泄漏测试规范流程、P02 项目出厂检验文档、SCPI 指令、网络继电器控制板软件、跟踪记录单 |
| Terminal\ | 测试记录.xlsx、设备升级.xmind |
| TestBench\ | 使用说明、智能测试平台修改计划表、兼容/升级修改方案、代码架构.xmind、测试业务流程图、错误编码整理 |
| interfaces\ | 上游系统接口对接.md/.pptx、接口差异.md |

另有：`11.APP\测试平台相关文档\03.测试任务结果保存防丢失记录.md`（与补传机制直接对应）

---

## 16. 迁移建议与注意事项

### 16.1 必须保留的核心行为

1. **离线韧性**：本地先写 + 待补传状态机 + 超时恢复，任何重构不得破坏该链路
2. **任务串行执行语义**：execTaskQueue 保证任务顺序执行，迁移时需等价实现（如 Channel + 单消费者）
3. **老化任务完整性判定**：CheckAgingTaskRemoteIsAllCorrectness 的 XML 解析与子项计数校验逻辑需完整移植
4. **重试原因分类**：RetryCategoryCode A/B/C/D 的业务含义需向业务方确认后在新区分实现
5. **设备连接策略**："首次手动确认，之后自动连接"的用户体验必须保留

### 16.2 技术栈映射建议

| 老平台 | 新平台建议 |
|---|---|
| .NET Framework 4.8 + WPF + MahApps.Metro | .NET 8 + WPF（或 Avalonia 跨平台）+ 现代 UI 库 |
| Caliburn.Micro / 自研 SimpleViewModel | CommunityToolkit.Mvvm（ObservableObject/RelayCommand） |
| static Queue + ContinueWith 任务调度 | System.Threading.Channels 或 HostedService 队列 |
| EF6 + SQLite/SQL Server 双上下文 | EF Core 8 + 双 DbContext + 迁移脚本 |
| Newtonsoft.Json | System.Text.Json（注意 Tasks.json 兼容性验证） |
| SOAP 服务引用（asmx/svc） | WCF Client(Core) 或 REST 化改造 + Polly 重试策略 |
| INI/XML 配置混合 | appsettings.json + Options 模式 |
| ScriptExecutor（C# 动态编译） | Roslyn Scripting（保留 RefAssemblies 机制） |

### 16.3 迁移风险点

- Xmas11.Comm 为自研闭源库，若不可获得源码需封装适配层隔离
- LSD 固件协议依赖精确的字节序与时序（40s 握手超时等），迁移后必须真机回归验证
- SOAP 服务端（内网 asmx/svc）在 .NET Core/.NET 5+ 下需使用 System.ServiceModel.Http 6.x 或 CoreWCF 客户端
- SQLite EnsureLocalDatabaseSchema 的手工 DDL 补丁逻辑应替换为 EF Migrations，但需兼容存量现场数据库（列已存在的场景）
- App.config 中的明文密码（softadmin/ConST12345）迁移时应引入密钥管理
