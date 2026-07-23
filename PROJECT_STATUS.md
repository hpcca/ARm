# AR 现实物体替换为 80 年代复古风模型项目状态说明

## 1. 项目路径与仓库

Unity 项目本地路径：

`F:\sem4\AR\chore\ARm`

GitHub 仓库：

`https://github.com/hpcca/ARm.git`

原 YOLO + Depth 方案开发分支：

`feature/multi-object-detection`

FoundationPose 扫描后静态替换方案分支：

`feature/foundationpose-static-scan`

最近已推送提交：

- `36bc167 Add depth-assisted replacement positioning`
- `82346b1 Improve multi-object retro replacements`
- `39eceaf Add replacement reacquisition state machine`
- `83f36cb Stabilize AR replacements after detection`

当前功能分支已推送到 GitHub。

仍未提交的本地未跟踪文件：

- `Assets/AR80sRetro/Models/monitor/`
- `Assets/AR80sRetro/Models/monitor.meta`
- `Assets/Main.unity`
- `Assets/Main.unity.meta`

这些未纳入当前功能分支提交，除非后续确认需要使用 monitor 或 Main 场景。

## 2. 项目目标

项目目标是实现一个 Unity AR 应用：

1. 使用手机摄像头实时检测现实物体。
2. 用 YOLO 识别物体类别。
3. 将识别到的现实物体替换为 80 年代复古风格 3D 模型。
4. 替换模型应尽量满足：
   - 能稳定生成；
   - 多个同类物体可以分别生成；
   - 位置尽量贴近真实物体；
   - scale 尽量接近真实物体大小；
   - 视角变化时模型不明显抖动；
   - 物体真实移动时模型能平滑跟随；
   - 后续希望支持真实物体遮挡虚拟模型的透视关系。

## 3. 当前已实现功能

### 3.1 YOLO 检测类别

当前 YOLO 支持以下目标：

- `cup`
- `phone`
- `tv`
- `bottle`
- `chair`
- `couch`
- `plant`
- `table`

其中：

- `plant` 对应 COCO 的 `potted plant`
- `table` 对应 COCO 的 `dining table`

相关脚本：

`Assets/AR80sRetro/Scripts/YoloObjectDetector.cs`

场景中 `maxDetections` 已从 `3` 提高到 `12`，避免多物体检测结果被截断。

### 3.2 Prefab 替换库

替换模型通过：

`Assets/AR80sRetro/Retro Prefab Library.asset`

进行配置。

已接入复古模型：

- cup
- phone
- tv
- bottle
- chair
- couch
- plant
- table

新增模型目录：

- `Assets/AR80sRetro/Models/bottle/`
- `Assets/AR80sRetro/Models/chair/`
- `Assets/AR80sRetro/Models/couch/`
- `Assets/AR80sRetro/Models/plant/`
- `Assets/AR80sRetro/Models/table/`

新模型的 FBX import 设置已做初步优化：

- 关闭无用 camera 导入；
- 关闭无用 light 导入；
- 关闭 animation 导入；
- 启用 mesh compression。

### 3.3 多同类物体替换

之前的问题：

同一类别只会生成一个替换模型，例如场景里有两个 cup，只会生成一个。

原因：

`RetroReplacementManager` 早期用 `Dictionary<label, Track>` 存替换模型，所以“一类只有一个 Track”。

当前修复：

`RetroReplacementManager` 已改为空间 Track 列表：

`Assets/AR80sRetro/Scripts/RetroReplacementManager.cs`

当前逻辑：

- 每个检测结果会根据 label 和 3D 距离匹配已有 Track；
- 同一帧中一个 Track 不会被多个 detection 重复匹配；
- 距离超过匹配半径时会创建新的 Track；
- 支持同类多实例；
- 短暂遮挡或低置信度不会立刻销毁已生成模型。

### 3.4 稳定性和重捕获逻辑

当前替换模型有状态机，大致包括：

- Searching
- Acquiring
- Locked
- TrackingMove
- Lost

已实现目标：

- 识别成功后模型锁定 scale 和 rotation；
- 置信度短暂下降时模型不会直接消失；
- 视角变化导致的小幅位置变化会被 dead zone 和 confirmation frames 抑制；
- 真正移动时模型会平滑移动；
- 未确认生成的临时 Track 会过期清理；
- 已生成模型默认不因 Lost 直接销毁。

### 3.5 朝向问题修正

之前的问题：

所有模型生成后都会朝向摄像头。

原因：

`ARRaycastPositionSolver` 在 raycast 到平面后强制：

`Quaternion.LookRotation(-toCamera)`

所以模型总是面向相机。

当前修复：

`ARRaycastPositionSolver.cs` 增加了 `faceCamera` 开关，默认关闭。

当前效果：

- 模型不再强制朝向摄像头；
- 但注意：仅靠 YOLO 2D 框 + AR 平面 raycast，仍无法准确恢复真实物体 yaw；
- 要真正获取物体朝向，需要 Depth/点云 ICP、CAD 配准、Vuforia Model Target 或 pose estimation 模型。

### 3.6 Scale 问题修正

之前的问题：

除 cup 外，tv、chair、couch、table、plant、bottle 等模型 scale 偏小或不符合真实物体大小。

原因：

早期只按 bbox 高度估算 scale。对 couch/table/chair 这类“宽但不高”的物体，会明显偏小。

当前修复：

`RetroReplacementRule.cs` 新增：

`ScaleBoundingBoxAxis`

可选：

- Height
- Width
- MaxDimension

`RetroReplacementManager.cs` 当前会同时计算 bbox 对应的世界宽度和高度，再按规则选择 scale。

当前规则：

- cup、bottle 更偏向高度估算；
- tv、chair、couch、plant、table 更偏向 `MaxDimension`；
- `Retro Prefab Library.asset` 中已给不同类别设置了第一轮 `scaleCalibrationMultiplier`、`estimatedHeightMultiplier`、`estimatedWidthMultiplier` 和 `scaleMultiplierRange`。

后续如果实机仍偏大/偏小，优先调：

`Retro Prefab Library.asset`

中对应 label 的：

- `scaleCalibrationMultiplier`
- `estimatedHeightMultiplier`
- `estimatedWidthMultiplier`
- `scaleMultiplierRange`

## 4. 阶段 3：Depth 辅助定位当前状态

已新增脚本：

`Assets/AR80sRetro/Scripts/ARDepthFrameProvider.cs`

当前实现：

- 使用 `AROcclusionManager`
- 请求 `EnvironmentDepthMode.Fastest`
- 开启 temporal smoothing
- 从 YOLO bbox 中心附近 5x5 区域采样 environment depth
- 有 confidence image 时过滤低可信 depth
- 取深度中位数
- 通过 camera viewport + depth 恢复 world point

`ARRaycastPositionSolver.cs` 当前融合逻辑：

- plane raycast 仍是兜底；
- depth 有效时，用 depth 恢复点修正 X/Z；
- Y 仍使用 plane raycast 的平面高度；
- depth 点和 plane 点水平差异过大时，降低 depth 权重，避免模型被异常深度拉飞。

场景接线：

`Assets/Scenes/SampleScene.unity`

在 `AR80sRetro System` 上已新增：

- `AROcclusionManager`
- `ARDepthFrameProvider`

并把 `ARRaycastPositionSolver.depthProvider` 指向该 provider。

验证结果：

`dotnet build Assembly-CSharp.csproj -nologo` 通过。

只有 Unity/.NET 常见警告：

- `System.Net.Http` 版本冲突
- `System.IO.Compression` 版本冲突

无 C# 编译错误。

## 5. 当前还未完成的问题

### 5.1 真实物体朝向仍不能准确恢复

当前只是避免模型总是朝向摄像头。

但真实物体自身 yaw 仍无法从 YOLO 2D 框准确获得。

可选方案：

1. **CAD / Model Target**
   - 精度高；
   - 适合固定物体；
   - 每个物体需要 CAD 或模型；
   - 可用 Vuforia Model Target。

2. **YOLO + Depth/LiDAR + ICP**
   - YOLO 找 bbox；
   - depth 截取点云；
   - 用 CAD/目标模型点云做 ICP 配准；
   - 输出 6DoF pose；
   - 科研价值高，但实现复杂度较高。

3. **Pose Estimation**
   - PoseCNN / DenseFusion / PVN3D / FoundationPose 等；
   - RGB 或 RGB-D 输入；
   - 能输出 position + rotation；
   - 精度高，但本地实时运行压力大。

4. **ARKit / ARCore Anchor**
   - 稳定放置模型；
   - 不能理解杯子/椅子真实朝向；
   - 适合“放置模型”，不适合“真实替换”。

当前项目短期建议：

先不做完整 pose estimation，优先把 Depth 辅助定位和遮挡做好。

### 5.2 真实遮挡虚拟模型尚未完成

当前 Depth 已用于辅助定位，但还没有完成“真实物体遮挡虚拟模型”的渲染遮挡。

当前遮挡问题表现：

当模型已经生成后，如果摄像头视角不变，但前景有真实物体遮挡部分视野，虚拟模型仍会完整显示，出现透视穿帮。

推荐方案：

1. **优先使用 ARFoundation Depth Occlusion**
   - 使用 `AROcclusionManager`
   - 开启 environment depth
   - 将 occlusion 作用到 AR Camera / URP 渲染管线
   - 让真实前景物体遮挡虚拟模型

2. **Depth 不可用时降级**
   - 如果检测框置信度下降、bbox 被遮挡或面积异常变小：
   - 不销毁模型；
   - 将模型 renderer 透明度平滑降低；
   - YOLO 恢复后再淡入。

后续建议：

下一阶段做“渲染遮挡”：

- 确认项目 URP/AR Foundation 当前渲染配置；
- 检查 AR Camera 上是否已有 `ARCameraBackground`；
- 将 `AROcclusionManager` 放到正确对象上；
- 配置 AR background / renderer feature 支持 environment depth occlusion；
- 实机验证真实手/物体是否能挡住虚拟模型。

### 5.3 Depth 坐标映射需要实机校准

`ARDepthFrameProvider` 中目前有两个可调字段：

- `flipDepthX`
- `flipDepthY`

当前默认：

- `flipDepthX = false`
- `flipDepthY = true`

这是根据当前 camera frame / screen 坐标经验设置的第一版。

如果实机测试发现 depth 修正方向反了，比如模型向相反方向偏移，需要尝试调整这两个开关。

建议测试方法：

1. 打开 `ARDepthFrameProvider.logDepthAvailability`
2. 对准一个静止 cup 或 bottle
3. 缓慢左右移动手机
4. 观察模型是否比之前更稳定
5. 如果模型水平位置反向偏移，调整 `flipDepthX` 或 `flipDepthY`

## 6. 下一步建议

### 优先级 1：实机测试阶段 3

测试目标：

- 设备支持 depth 时，走动观察物体，模型不应跟着 bbox 明显抖动；
- 手动移动真实物体后，模型能较平滑跟随；
- depth 不可用时，系统应自动回退到 plane raycast，不应报错或停止生成模型。

重点测试对象：

- cup
- phone
- bottle
- chair/table/couch 这类大物体

重点观察：

- 模型是否比之前更稳定；
- depth 是否导致位置偏移；
- 不同距离下 scale 是否合理；
- 如果 depth 无效，Console 是否频繁报错。

### 优先级 2：开启真实遮挡渲染

目标：

真实物体或手遮挡镜头时，虚拟模型应被真实前景遮住，而不是完整浮在前面。

推荐实现方向：

- 正确配置 `AROcclusionManager`
- 接入 AR Camera background / URP occlusion
- 必要时增加 renderer visibility fallback：
  - ProbablyOccluded
  - HiddenButTracked
  - Visible

### 优先级 3：继续优化 scale 和 anchor

不同类别应继续调：

- `raycastAnchorInBoundingBox`
- `scaleCalibrationMultiplier`
- `estimatedHeightMultiplier`
- `estimatedWidthMultiplier`
- `scaleMultiplierRange`

建议实机测试后记录表格：

| label | 当前问题 | 建议调参 |
|---|---|---|
| cup | 是否稳定 | calibration |
| bottle | 是否偏小/偏大 | height multiplier |
| chair | 是否贴地 | anchor / MaxDimension |
| couch | 是否偏小 | width multiplier |
| table | 是否高度不准 | anchor / scale |
| plant | 是否漂浮 | vertical offset |

## 7. 阶段 4：Depth Occlusion 渲染与 fallback

### 7.1 场景与渲染接线修正

已确认当前工程为：

- Unity `2022.3.62f3`
- URP `14.0.12`
- AR Foundation / ARCore / ARKit `5.2.0`

`Assets/Settings/URP-Performant-Renderer.asset` 已包含并启用
`ARBackgroundRendererFeature`，因此不需要再增加自定义 URP Renderer Feature。

之前 `AROcclusionManager` 挂在 `AR80sRetro System` 上，只能被
`ARDepthFrameProvider` 用于 CPU depth 采样，无法被 `ARCameraBackground`
自动用于背景深度写入。现已修正为：

- `AROcclusionManager` 挂在 `XR Origin (AR Rig) / Camera Offset / Main Camera`
  的 Camera、`ARCameraManager`、`ARCameraBackground` 同一 GameObject 上；
- `ARDepthFrameProvider` 仍留在 `AR80sRetro System`，并引用 Camera 上的
  `AROcclusionManager`；
- `ARRaycastPositionSolver.depthProvider` 仍引用该 provider；
- `RetroReplacementManager.depthProvider` 也引用该 provider，用于判断是否需要 fallback。

该组件作为 `SampleScene` 中 XR Origin Prefab 实例的场景级新增组件保存，
没有修改 XRI Samples 的源 Prefab。相关接线集中在：

- `Assets/Scenes/SampleScene.unity`

### 7.2 Environment Depth Occlusion

当前配置：

- Environment Depth Mode：`Fastest`
- Environment Depth Temporal Smoothing：开启
- Occlusion Preference：`PreferEnvironmentOcclusion`
- URP `ARBackgroundRendererFeature`：已启用

`ARDepthFrameProvider` 现在不仅检查 requested/current mode，还会检查：

- 当前设备的 occlusion subsystem 是否声明支持 environment depth；
- `AROcclusionManager` 是否启用；
- environment depth texture 是否已实际产生。

只有以上条件全部满足时，才把 depth 视为可用于真实遮挡。

### 7.3 无 Depth / 低置信度淡出 fallback

`RetroReplacementManager` 已增加运行时淡出降级：

- depth 实际可用时，保持模型正常不透明，由 AR Foundation depth occlusion
  负责真实前景遮挡；
- depth 不可用时，如果 YOLO 置信度低于 tracking threshold，或连续一段时间
  没有可靠检测，模型会平滑淡出；
- 默认最低透明度为 `0.2`，不会立即销毁已锁定模型；
- 检测恢复后模型平滑淡入；
- 只克隆并修改生成实例的运行时材质，不修改 FBX/Prefab 使用的共享材质资产；
- 运行时材质会在替换实例清理或 manager 销毁时释放。

当前 `SampleScene` 参数：

- `depthStartupGraceSeconds = 2`
- `depthAvailabilityGraceSeconds = 0.5`（由 `ARDepthFrameProvider` 抑制偶发 depth frame 丢失）
- `fallbackFadeDelaySeconds = 0.35`
- `fallbackFadeDurationSeconds = 0.35`
- `fallbackMinimumOpacity = 0.2`

Android 的 ARCore Depth 已从 Required 改为 Optional：

`Assets/XR/Settings/AR Core Settings.asset`

这样不支持 Depth API 的设备仍可安装并运行，再自动进入淡出 fallback。

### 7.4 验证结果与实机检查

`dotnet build Assembly-CSharp.csproj -nologo` 已通过：

- `0` error
- 仅保留既有 `System.Net.Http` 与 `System.IO.Compression` 版本冲突警告

仍需实机验证：

1. 支持 depth 的 Android：把手或近处真实物体移到模型前，确认模型对应像素被遮挡；
2. iOS：environment depth 只在支持 scene depth 的设备配置上可用；
3. 不支持 depth 的设备：遮挡导致检测下降后，确认模型淡出至约 20%，恢复后淡入；
4. 继续校准 `flipDepthX` / `flipDepthY`，因为 CPU depth 定位映射仍依赖设备方向和相机裁切；
5. 如果实机 depth 边缘噪声明显，可把 Environment Depth Mode 从 `Fastest`
   调到 `Medium`，再评估帧率与遮挡质量。

本阶段没有改动或纳入：

- `Assets/AR80sRetro/Models/monitor/`
- `Assets/Main.unity`

## 8. 新方案：FoundationPose 扫描后静态替换

### 8.1 方案决策

由于 YOLO 2D bbox + AR 平面 + 局部 depth 无法可靠恢复现实物体的完整 6DoF
姿态，也无法区分相机视角变化和物体真实移动，下一条实验路线调整为：

1. Android 手机在同一个 ARSession 内绕目标物体扫描；
2. 采集 RGB、environment depth、confidence、相机内参、时间戳和拍摄时相机世界位姿；
3. 扫描结束后把多视角数据交给 NVIDIA PC；
4. PC 端 FoundationPose 使用真实物体 CAD/3D 扫描和第一帧 mask 做 registration，
   再对后续关键帧 tracking；
5. 把每帧 `cameraFromObject` 变换到 ARCore 世界坐标并做多帧融合；
6. 结果传回 Android；
7. 用户确认后在 `ARAnchor` 下生成静态复古替换模型。

第一阶段只处理一个已知、刚性、非高度对称且保持静止的物体。实时移动物体、
多物体并发和跨会话恢复留到后续阶段。

详细计划：

- `Docs/FoundationPose/STATIC_SCAN_PLAN.md`
- `Docs/FoundationPose/ENVIRONMENT_SETUP.md`

### 8.2 当前环境检查

已确认：

- RTX 4060 Laptop GPU，8 GB 显存；
- Windows NVIDIA 驱动 591.44；
- WSL 2.6.1.0；
- 已安装 Ubuntu 22.04.5 LTS，并确认为 WSL 2；
- Ubuntu 内可以通过 `nvidia-smi` 看到 RTX 4060；
- Ubuntu 内已有 Python 3；
- Docker Engine 29.6.2；
- NVIDIA Container Toolkit 1.19.1；
- CUDA 12.4.1 基础容器已成功通过 `--gpus all` 访问 RTX 4060。

尚未完成：

- FoundationPose 官方镜像、权重和 demo 数据；
- 官方 demo 实机跑通与显存/耗时记录。

### 8.3 重构边界

- 新增一个显式扫描 Bootstrap，不新增全局单例；
- 采集、关键帧选择、网络、坐标转换/融合、静态生成分开实现；
- 第一版延续当前 Inspector 显式接线，不急于增加 asmdef；
- 扫描模式不使用 `ARRaycastPositionSolver` 决定最终位姿；
- 扫描模式不使用 bbox 推断最终 scale/rotation；
- 旧 YOLO 动态替换流程保留为实验基线，不立即删除；
- 继续复用 Main Camera 上的 `AROcclusionManager` 做 environment depth occlusion；
- 模型权重、扫描数据和 FoundationPose 第三方源码不提交 Git。

### 8.4 受保护的未跟踪内容

新方案分支仍不纳入：

- `Assets/AR80sRetro/Models/monitor/`
- `Assets/AR80sRetro/Models/monitor.meta`
- `Assets/Main.unity`
- `Assets/Main.unity.meta`

除非后续明确选择 monitor 作为首个 FoundationPose 目标物体并确认其资产来源、
CAD 匹配和授权，否则保持未跟踪状态。
