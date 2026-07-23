[CmdletBinding()]
param(
    [string]$Distro = "Ubuntu-22.04",
    [string]$CudaImage = "nvidia/cuda:12.4.1-base-ubuntu22.04"
)

$ErrorActionPreference = "Continue"

function Write-Check {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    $status = if ($Passed) { "PASS" } else { "FAIL" }
    Write-Host ("[{0}] {1}: {2}" -f $status, $Name, $Detail)
}

$nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
$nvidiaDetail = if ($nvidiaSmi) { $nvidiaSmi.Source } else { "nvidia-smi not found" }
Write-Check "Windows NVIDIA driver" ($null -ne $nvidiaSmi) $nvidiaDetail

$wsl = Get-Command wsl -ErrorAction SilentlyContinue
if ($null -eq $wsl) {
    Write-Check "WSL" $false "wsl.exe not found"
    exit 1
}

$distros = @(wsl --list --quiet 2>$null) |
    ForEach-Object { ($_ -replace "`0", "").Trim() } |
    Where-Object { $_ }

$hasDistro = $distros -contains $Distro
$distroDetail = if ($hasDistro) { $Distro } else { "$Distro not registered" }
Write-Check "WSL distro" $hasDistro $distroDetail

if (-not $hasDistro) {
    exit 1
}

$versionLine = (wsl --list --verbose 2>$null |
    ForEach-Object { $_ -replace "`0", "" } |
    Where-Object { $_ -match [regex]::Escape($Distro) } |
    Select-Object -First 1)
$isWsl2 = $versionLine -match "\s2\s*$"
$versionDetail = if ($versionLine) { $versionLine.Trim() } else { "version unavailable" }
Write-Check "WSL version" $isWsl2 $versionDetail

wsl -d $Distro -- bash -lc "test -x /usr/lib/wsl/lib/nvidia-smi"
$hasGpuTool = $LASTEXITCODE -eq 0
$gpuDetail = if ($hasGpuTool) { "/usr/lib/wsl/lib/nvidia-smi" } else { "not found" }
Write-Check "WSL GPU bridge" $hasGpuTool $gpuDetail

if ($hasGpuTool) {
    wsl -d $Distro -- /usr/lib/wsl/lib/nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader
}

wsl -d $Distro -- bash -lc "command -v python3 >/dev/null"
Write-Check "WSL Python 3" ($LASTEXITCODE -eq 0) "python3"

wsl -d $Distro -- bash -lc "command -v docker >/dev/null"
$hasDocker = $LASTEXITCODE -eq 0
$dockerDetail = if ($hasDocker) { "installed" } else { "not installed" }
Write-Check "Docker" $hasDocker $dockerDetail

wsl -d $Distro -- bash -lc "command -v nvcc >/dev/null"
$hasNvcc = $LASTEXITCODE -eq 0
if ($hasNvcc) {
    Write-Check "CUDA Toolkit compiler" $true "nvcc installed on WSL host"
} else {
    Write-Host "[INFO] CUDA Toolkit compiler: not installed on host; Docker path does not require it"
}

if ($hasDocker) {
    wsl -d $Distro -- bash -lc "docker info >/dev/null 2>&1"
    $hasDockerDaemon = $LASTEXITCODE -eq 0
    $daemonDetail = if ($hasDockerDaemon) { "reachable" } else { "not reachable" }
    Write-Check "Docker daemon" $hasDockerDaemon $daemonDetail

    if ($hasDockerDaemon) {
        wsl -d $Distro -- docker image inspect $CudaImage *> $null
        $hasCudaImage = $LASTEXITCODE -eq 0
        $imageDetail = if ($hasCudaImage) { $CudaImage } else { "$CudaImage not pulled" }
        Write-Check "CUDA test image" $hasCudaImage $imageDetail

        if ($hasCudaImage) {
            wsl -d $Distro -- docker run --rm --gpus all $CudaImage `
                nvidia-smi `
                --query-gpu=name,memory.total,driver_version `
                --format=csv,noheader
            Write-Check "Docker GPU access" ($LASTEXITCODE -eq 0) (
                "docker run --gpus all"
            )
        }
    }
}
