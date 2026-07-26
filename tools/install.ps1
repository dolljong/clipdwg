<#
.SYNOPSIS
    clipdwg 를 빌드해서 AutoCAD 자동 로드 폴더에 설치한다.

.DESCRIPTION
    %APPDATA%\Autodesk\ApplicationPlugins\clipdwg.bundle 로 배포한다.
    설치 후 AutoCAD를 다시 켜면 CLIPDWG / CLIPDWGCFG 명령을 바로 쓸 수 있다.
    (NETLOAD 를 매번 칠 필요가 없어진다.)

.PARAMETER Configuration
    빌드 구성. 기본 Release.

.PARAMETER Uninstall
    설치된 번들을 지운다.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$bundleName = 'clipdwg.bundle'
$target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\$bundleName"

if ($Uninstall) {
    if (Test-Path $target) {
        Remove-Item -Recurse -Force $target
        Write-Host "제거 완료: $target"
    } else {
        Write-Host "설치되어 있지 않습니다: $target"
    }
    return
}

Write-Host "빌드 중 ($Configuration)..."
& dotnet build (Join-Path $repoRoot 'ClipDwg.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (exit $LASTEXITCODE)" }

$outDir = Join-Path $repoRoot "src\ClipDwg\bin\$Configuration\net48"
$required = @('ClipDwg.dll', 'ClipDwg.Core.dll')
foreach ($file in $required) {
    $path = Join-Path $outDir $file
    if (-not (Test-Path $path)) { throw "빌드 산출물을 찾을 수 없습니다: $path" }
}

$contents = Join-Path $target 'Contents'

# AutoCAD가 NETLOAD 로 DLL을 잡고 있으면 덮어쓸 수 없다. 원인을 분명히 알려 준다.
foreach ($file in $required) {
    $installed = Join-Path $contents $file
    if (-not (Test-Path $installed)) { continue }
    try {
        $stream = [System.IO.File]::Open($installed, 'Open', 'ReadWrite', 'None')
        $stream.Close()
    } catch {
        $acad = Get-Process acad -ErrorAction SilentlyContinue
        $who = if ($acad) { "AutoCAD (PID $($acad.Id -join ', '))" } else { '다른 프로세스' }
        throw "$file 을(를) $who 가 사용 중이라 설치할 수 없습니다.`n" +
              "AutoCAD를 닫고 다시 실행하세요. (NETLOAD 한 어셈블리는 AutoCAD 종료 전까지 잠깁니다.)"
    }
}

# 폴더를 통째로 지우면 백신·탐색기가 잠깐 붙잡고 있을 때 실패하고, 그러면 파일만 지워진
# 반쪽 상태로 남는다(PackageContents.xml 은 있는데 DLL 이 없는 상태). 덮어쓰기로 처리한다.
New-Item -ItemType Directory -Force -Path $contents | Out-Null

function Copy-WithRetry([string]$from, [string]$to) {
    for ($i = 1; $i -le 5; $i++) {
        try { Copy-Item $from $to -Force; return } catch { Start-Sleep -Milliseconds 300 }
    }
    Copy-Item $from $to -Force   # 마지막 시도는 예외를 그대로 올린다
}

Copy-WithRetry (Join-Path $repoRoot 'package\PackageContents.xml') $target
foreach ($file in $required) {
    Copy-WithRetry (Join-Path $outDir $file) $contents
}

# 디버깅용 심볼이 있으면 같이 넣는다 (없어도 무방)
Get-ChildItem $outDir -Filter '*.pdb' -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-WithRetry $_.FullName $contents }

# 예전 버전이 남긴 파일 정리 (실패해도 설치 자체에는 지장 없음)
$keep = $required + (Get-ChildItem $outDir -Filter '*.pdb' -ErrorAction SilentlyContinue | ForEach-Object Name)
Get-ChildItem $contents -File | Where-Object { $keep -notcontains $_.Name } | ForEach-Object {
    try { Remove-Item $_.FullName -Force } catch { Write-Warning "정리 실패(무시): $($_.Name)" }
}

Write-Host ""
Write-Host "설치 완료: $target"
Write-Host "AutoCAD를 다시 시작한 뒤 CLIPDWG 를 실행하세요."
Write-Host "설정은 CLIPDWGCFG, 설정 파일은 $env:APPDATA\clipdwg\settings.json 입니다."
