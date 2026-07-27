<#
.SYNOPSIS
    clipdwg 를 빌드해서 배포용 zip 을 만든다.

.DESCRIPTION
    dist\clipdwg-<버전>.zip 을 만든다. zip 안에는 clipdwg.bundle 폴더 하나가 들어 있어서,
    받는 사람은 %APPDATA%\Autodesk\ApplicationPlugins 에 풀기만 하면 된다.
    버전은 package\PackageContents.xml 의 AppVersion 을 그대로 쓴다.

.PARAMETER Configuration
    빌드 구성. 기본 Release.

.PARAMETER OutDir
    zip 을 놓을 폴더. 기본 <repo>\dist.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\pack.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'dist' }

$manifest = Join-Path $repoRoot 'package\PackageContents.xml'
$version = ([xml](Get-Content $manifest)).ApplicationPackage.AppVersion
if (-not $version) { throw "PackageContents.xml 에서 AppVersion 을 읽지 못했습니다." }

Write-Host "빌드 중 ($Configuration)..."
& dotnet build (Join-Path $repoRoot 'ClipDwg.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (exit $LASTEXITCODE)" }

$outBin = Join-Path $repoRoot "src\ClipDwg\bin\$Configuration\net48"
$required = @('ClipDwg.dll', 'ClipDwg.Core.dll')
foreach ($file in $required) {
    $path = Join-Path $outBin $file
    if (-not (Test-Path $path)) { throw "빌드 산출물을 찾을 수 없습니다: $path" }
}

# 이전에 만든 스테이징이 남아 있으면 지운 파일이 zip 에 섞여 들어간다.
$stage = Join-Path $OutDir 'clipdwg.bundle'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$contents = Join-Path $stage 'Contents'
New-Item -ItemType Directory -Force -Path $contents | Out-Null

Copy-Item $manifest $stage
foreach ($file in $required) { Copy-Item (Join-Path $outBin $file) $contents }

# pdb 는 넣지 않는다. 배포본에서 쓸 일이 없고 zip 만 두 배로 키운다.
$zip = Join-Path $OutDir "clipdwg-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip

Remove-Item -Recurse -Force $stage

$size = [math]::Round((Get-Item $zip).Length / 1KB)
Write-Host ""
Write-Host "생성 완료: $zip ($size KB)"
Write-Host "버전 $version — 받는 사람은 %APPDATA%\Autodesk\ApplicationPlugins 에 풀면 됩니다."
