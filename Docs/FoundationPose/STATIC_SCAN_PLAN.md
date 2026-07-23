# FoundationPose 扫描后静态替换方案

## 决策

新方案采用：

`Android ARCore 扫描 -> RGB-D 数据上传 -> NVIDIA PC FoundationPose ->`
`6DoF 位姿回传 -> Unity ARAnchor 静态生成复古模型`

FoundationPose 返回的是“真实目标物体相对于拍摄相机”的位姿。Unity 使用每张
关键帧拍摄时保存的 ARCore 相机世界位姿，将结果转换到当前 ARSession 世界坐标：

```text
worldFromObject =
    worldFromCameraAtCapture
    * unityFromCv
    * cameraFromObject
    * objectAxisCorrection
```

最终复古模型位姿：

```text
worldFromRetro = worldFromObject * objectToRetro
```

第一阶段不依赖 AR 平面高度，也不再从 YOLO bbox 推断最终 scale 和 rotation。

## 项目层级

当前按研究原型处理。目标是先把一个已知、刚性、非高度对称物体完整跑通，
不为尚未验证的多物体实时跟踪设计大型框架。

## 第一阶段范围

包含：

- 同一 ARSession 内完成扫描、PC 处理和静态显示
- 单个已知真实物体
- 一个精确 CAD 或高质量 3D 扫描模型
- 10 至 20 张 RGB-D 关键帧，至少覆盖 3 个视角
- 第一帧 mask；后续帧使用 FoundationPose tracking
- 多帧 6DoF 结果异常值剔除与融合
- 用户确认、重扫和小幅位姿微调
- `ARAnchor` 下生成一个静态复古替换模型
- 延续现有 environment depth occlusion 与无 depth 淡出能力

暂不包含：

- 真实物体移动后的实时跟随
- 多个物体并行 FoundationPose tracking
- 跨 ARSession 恢复
- 房间 mesh 重建或完整数字孪生
- 把 FoundationPose 直接移植到 Unity Sentis / Android

## 推荐模块

第一版保持当前项目的 Inspector 显式引用风格，不增加全局单例，也暂不增加项目
自有 asmdef。

1. `FoundationPoseScanController`
   - 唯一扫描入口和状态机。
   - 顺序初始化采集、上传、处理、确认和生成。
2. `ARRgbdScanFrameCollector`
   - 采集原始 RGB、environment depth、confidence、相机内参、时间戳和相机世界位姿。
3. `ScanKeyframeSelector`
   - 根据 AR tracking、相机位移/转角、目标面积和 depth 有效率筛关键帧。
4. `FoundationPoseClient`
   - 负责 HTTP 健康检查、扫描上传、处理启动、轮询和结果下载。
5. `WorldPoseFusionService`
   - 纯 C#；负责坐标转换、离群值剔除、位置/旋转融合和质量评分。
6. `StaticReplacementSpawner`
   - 创建 `ARAnchor`，应用 `objectToRetro`，只生成并锁定静态实例。
7. `FoundationPoseObjectProfile`
   - ScriptableObject；保存 CAD ID、单位、轴向、真实尺寸、mask 策略和
     `objectToRetro` 标定。

## 数据所有权

- 场景对象：AR Foundation 组件、扫描 Bootstrap、UI 和显式组件引用。
- ScriptableObject：服务器配置、物体/CAD 配置、复古模型对齐配置。
- 纯 C#：协议 DTO、坐标系转换、关键帧评分、位姿融合。
- 手机运行时缓存：RGB-D 帧和扫描会话，不放进 Unity `Assets/`。
- PC 运行时缓存：上传扫描、mask、FoundationPose 中间结果和日志，不提交 Git。

## 场景契约

```text
XR Origin (AR Rig)
└── Camera Offset
    └── Main Camera
        ├── Camera
        ├── ARCameraManager
        ├── ARCameraBackground
        └── AROcclusionManager

FoundationPose Scan System
├── FoundationPoseScanController
├── ARRgbdScanFrameCollector
├── FoundationPoseClient
├── StaticReplacementSpawner
└── Static Replacement Root

Scan UI
├── Start Scan
├── Finish Scan
├── Progress
├── Accept
├── Rescan
└── Error
```

继续保留 `AROcclusionManager` 在 Main Camera 上。`ARDepthFrameProvider` 可以继续
引用它，但不能再把最终物体位置强制投影到 AR 平面。

## 扫描状态机

```text
Idle
 -> CheckingPc
 -> Scanning
 -> Uploading
 -> Processing
 -> Reviewing
 -> Locked
```

任一网络、mask、depth 或 pose 质量失败都进入可重试错误态。数据不足时提示重扫，
不回退成旧的 Plane Raycast 最终位姿，以免再次生成“物体在地板下、模型浮在地板上”
的错误结果。

## 采集格式

```text
scan_<id>/
├── rgb/
├── depth/
├── confidence/
├── masks/
├── metadata/
└── scan.json
```

每个关键帧必须保存：

- frame index 与相机时间戳
- RGB/depth 原始尺寸
- 相机内参 `fx/fy/cx/cy`
- `worldFromCameraAtCapture` 4x4 矩阵
- 屏幕旋转、图像旋转与镜像信息
- YOLO label、bbox、confidence
- AR tracking state
- depth 有效像素比例

现有 `ARCameraFrameProvider` 会缩放到 640x640，并执行旋转/镜像，而且不保存完整
内参、时间戳和相机位姿，因此不能直接作为 FoundationPose 数据采集器。

## 服务端协议

原型先使用 HTTP 批处理：

- `GET /health`
- `POST /scans`
- `POST /scans/{scanId}/frames`
- `POST /scans/{scanId}/process`
- `GET /scans/{scanId}/status`
- `GET /scans/{scanId}/result`

第一版先让 Unity 把扫描目录离线复制到 PC 并跑通 pose；确认数据格式正确后再接入
HTTP，避免同时调试 RGB-D 对齐、坐标系和网络。

## 与旧流程的关系

扫描模式启用时：

- 禁用 `RetroDetectionPipeline` 对替换模型的动态生成；
- 不使用 `ARRaycastPositionSolver` 决定最终 pose；
- 不使用 bbox 世界尺寸决定最终 scale；
- YOLO 只负责候选类别、bbox 和 mask 引导；
- 旧流程保留为可切换的实验基线，不立即删除。

## 首阶段验收

- 官方 FoundationPose demo 在本机 RTX 4060 上成功。
- Android 导出的 RGB、depth、内参和相机 pose 可以在 PC 端成对读取。
- 单物体扫描至少得到 10 张合格关键帧和 3 个视角。
- PC 返回的 4x4 pose 在 Unity 中轴向、手性、矩阵顺序和单位正确。
- 不依赖错误 AR 平面也能生成模型。
- 用户走动观察时模型稳定留在真实物体位置。
- YOLO 暂时丢失不会删除已锁定模型。
- 支持 depth 的设备仍具有真实前景遮挡。
- 初期重复扫描目标：位置误差约 5 cm，朝向误差约 5 至 10 度。

## 实现顺序

1. 跑通官方 demo，记录显存与处理时间。
2. 选定首个真实物体、CAD 和复古 prefab，完成轴向/单位标定。
3. 实现离线 `ARRgbdScanFrameCollector` 和元数据导出。
4. 在 PC 脚本中读取一组 Android 数据并运行 FoundationPose。
5. 实现坐标转换单元测试和多帧融合。
6. Unity 静态生成 + `ARAnchor` + 用户确认。
7. 最后接 HTTP 上传和 UI 状态机。
