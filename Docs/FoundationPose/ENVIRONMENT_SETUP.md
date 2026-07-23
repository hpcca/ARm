# FoundationPose 环境基线与配置

检查日期：2026-07-23

## 当前结论

这台电脑可以作为第一阶段“扫描后单个静态物体位姿估计”的原型服务器。
WSL2、Docker 与容器 GPU 访问已经配置完成；FoundationPose 本体、权重和 demo
数据仍待安装。

已确认：

- GPU：NVIDIA GeForce RTX 4060 Laptop GPU，8 GB 显存
- Windows NVIDIA 驱动：591.44
- 驱动报告的 CUDA 兼容版本：13.1
- WSL：2.6.1.0，Linux kernel 6.6.87.2
- 已安装：Ubuntu 22.04.5 LTS，WSL 2
- WSL 内可以通过 `nvidia-smi` 看到 RTX 4060
- WSL 内已有 `/usr/bin/python3`
- Docker Engine 29.6.2
- NVIDIA Container Toolkit 1.19.1
- CUDA 12.4.1 Ubuntu 22.04 基础容器可通过 `--gpus all` 访问 RTX 4060

尚未完成：

- Ubuntu 内尚未配置项目专用普通用户
- FoundationPose 仓库、镜像、权重与 demo 数据尚未下载
- FoundationPose 官方 demo 尚未在本机跑通

WSL host 没有安装 `nvcc`。当前选择 Docker 路线，因此 host `nvcc` 不是
FoundationPose 的阻塞项；构建 CUDA 扩展所需的 toolkit 应由 FoundationPose
容器提供。

Windows 路径中的 `python.exe` 只是 Microsoft Store 别名，不能作为有效的
FoundationPose Python 环境。服务端依赖统一安装在 WSL2 Ubuntu 内，不在
Windows Python 中混装。

## 推荐环境路线

第一阶段优先采用 FoundationPose 官方推荐的 Docker 路线：

1. Windows 只保留 NVIDIA 显卡驱动。
2. FoundationPose、Docker、CUDA 用户态依赖运行在 WSL2 Ubuntu 22.04 内。
3. 不在 WSL 内安装 Linux NVIDIA 显卡驱动；WSL 使用 Windows 驱动映射的
   `libcuda.so`。
4. 官方 FoundationPose 仓库放在仓库外，或放在被忽略的
   `External/FoundationPose/`，避免把第三方源码、权重和大体积数据提交到
   Unity 仓库。
5. Unity 项目只保存通信协议、采集代码、位姿转换代码和轻量服务端桥接代码。

官方参考：

- FoundationPose：https://github.com/NVlabs/FoundationPose
- WSL 安装：https://learn.microsoft.com/windows/wsl/install
- NVIDIA CUDA on WSL：
  https://docs.nvidia.com/cuda/wsl-user-guide/index.html

## 下一安装检查点

以下步骤中，Docker/GPU 基础已经完成；开始 Unity RGB-D 采集重构前仍需完成
FoundationPose demo 检查：

1. 启动 Ubuntu 22.04，创建普通 Linux 用户。
2. 拉取 FoundationPose 官方镜像，进入容器后执行一次 `build_all.sh`。
3. 下载官方权重和 demo 数据。
4. 运行官方 `run_demo.py`，确认：
   - 能完成第一帧 registration；
   - 能完成后续帧 tracking；
   - 能输出 object-in-camera 的 4x4 pose 矩阵；
   - 峰值显存没有超过 8 GB。

已通过的 CUDA 容器验证命令：

```bash
docker run --rm --gpus all \
  nvidia/cuda:12.4.1-base-ubuntu22.04 nvidia-smi
```

如果官方容器在 8 GB 显存上 OOM，第一阶段先降低输入分辨率、减少并行任务，
并只处理单物体；不要先扩大到多物体。

仓库提供了按 Docker 与 NVIDIA 官方 apt 仓库配置的辅助脚本：

```powershell
wsl -d Ubuntu-22.04 -u root -- bash `
  /mnt/f/sem4/AR/chore/ARm/Tools/FoundationPose/install_docker_wsl.sh
```

该脚本会安装 Docker Engine、Docker Compose plugin 与 NVIDIA Container
Toolkit，并启用 Docker systemd 服务。它不会安装 Linux NVIDIA 显卡驱动。

## 可重复检查

Windows PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass `
  -File .\Tools\FoundationPose\Test-FoundationPoseHost.ps1
```

预期最终全部通过：

- Windows `nvidia-smi`
- WSL2 Ubuntu 22.04
- WSL GPU passthrough
- Docker
- Docker GPU access
- Python 3

## 配置边界

- 不把模型权重、扫描 RGB-D 数据、结果缓存提交到 Git。
- 不把手机与 PC 的 IP 地址硬编码到场景或脚本。
- 服务 URL、超时、重试次数后续放在
  `FoundationPoseServerConfig` 配置资产中。
- CUDA、PyTorch 与第三方扩展版本以官方容器为准，不以 Windows 驱动显示的
  “CUDA Version 13.1”作为必须安装 CUDA 13.1 Toolkit 的依据。
