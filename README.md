# clipdwg

[![Download](https://img.shields.io/github/v/release/dolljong/clipdwg?label=download&color=brightgreen)](https://github.com/dolljong/clipdwg/releases/latest)
![AutoCAD 2024](https://img.shields.io/badge/AutoCAD-2024%20(R24.3)-red)
![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)

AutoCAD에서 선택한 객체를 **벡터 그래픽(EMF)** 으로 클립보드에 복사하는 플러그인입니다.
Word·PowerPoint·한글에 붙여넣으면 확대해도 깨지지 않고, 그룹해제해서 편집할 수 있습니다.

```
객체 선택 → CLIPDWG → 문서에 Ctrl+V
```

## 왜 만들었나

AutoCAD 기본 복사(`Ctrl+C`)로도 문서에 붙일 수는 있지만, 실무에서 걸리는 게 몇 가지 있습니다.

| | AutoCAD 기본 복사 | clipdwg |
|---|---|---|
| 선 굵기 | 화면 표시 그대로 | **색상별로 mm 단위 지정** |
| 흰색 선 | 흰 배경에서 안 보임 | 자동으로 검정 변환 |
| 붙여넣기 크기 | 예측하기 어려움 | mm 단위로 정확히 제어 |
| 배경 | 도면 배경색이 딸려 옴 | 배경 없음 |
| 텍스트 | 경우에 따라 이미지화 | **글자로 남아 편집·검색 가능** |

<!-- 스크린샷: 도면 선택 화면과 Word 붙여넣기 결과를 나란히 넣으면 좋습니다 -->

## 요구사항

- AutoCAD 2024 (R24.3), Windows x64
- 빌드해서 쓸 때만: .NET Framework 4.8 개발 도구 (Visual Studio 2022 또는 Build Tools)

AutoCAD 2025 이상은 [아래](#autocad-2025-이상) 참고.

## 설치

### 방법 1 — 빌드된 파일 받기 (권장)

[**Releases 에서 `clipdwg-*.zip` 다운로드**](https://github.com/dolljong/clipdwg/releases/latest) 후,
AutoCAD를 닫고 PowerShell에서:

```powershell
cd $env:USERPROFILE\Downloads
Unblock-File clipdwg-1.0.0.zip                                             # 다운로드 잠금 해제
Expand-Archive clipdwg-1.0.0.zip "$env:APPDATA\Autodesk\ApplicationPlugins" -Force
```

압축을 직접 풀어 `clipdwg.bundle` 폴더를
`%APPDATA%\Autodesk\ApplicationPlugins` 에 넣어도 똑같습니다.

> `Unblock-File` 을 건너뛰면 인터넷에서 받은 표시(Mark of the Web)가 DLL에 남아 AutoCAD가
> 로드를 막거나 보안 경고를 띄웁니다. 이미 풀었다면
> `Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\clipdwg.bundle" -Recurse | Unblock-File`.

### 방법 2 — 소스에서 빌드

```powershell
git clone https://github.com/dolljong/clipdwg.git
cd clipdwg
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

### 공통

두 방법 모두 `%APPDATA%\Autodesk\ApplicationPlugins\clipdwg.bundle` 로 설치됩니다.
AutoCAD를 다시 켜면 명령을 바로 쓸 수 있습니다. 명령을 처음 칠 때 로드되므로 AutoCAD
시작 시간은 늘어나지 않습니다.

제거는 그 폴더를 지우면 됩니다. 소스로 설치했다면:

```powershell
powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Uninstall
```

> AutoCAD가 실행 중이면 DLL이 잠겨 설치에 실패합니다. `install.ps1` 은 어떤 프로세스가
> 잡고 있는지 알려 주니, 닫고 다시 실행하세요.

## 사용법

| 명령 | 하는 일 |
|---|---|
| `CLIPDWG` | 선택한 객체를 EMF로 클립보드에 복사 |
| `CLIPDWGCFG` | 색상별 선두께·글꼴·축척 옵션 편집 |

객체를 먼저 골라 놓고 `CLIPDWG` 를 쳐도 되고, 명령을 먼저 친 뒤 골라도 됩니다.

실행하면 명령행에 이렇게 찍힙니다.

```
clipdwg: 대상 47개 (Line 28, Arc 4, LWPolyline 9, 치수 6)
clipdwg: 무시 3개 (Hatch 2, BlockReference 1)
clipdwg: 도형 61개를 클립보드에 복사했습니다 (184.32 x 97.05 mm, 프로파일 'default').
```

## 지원 객체

| 지원 | 비고 |
|---|---|
| 선 (LINE) | |
| 호 (ARC) · 원 (CIRCLE) | |
| 폴리라인 (LWPOLYLINE, POLYLINE 2D/3D) | 곡선(bulge)과 폭 지원 |
| 텍스트 (TEXT, MTEXT) | 글자로 남음 |
| 치수 (DIMENSION 전 종류) | 선형·정렬·반지름·지름·각도·좌표·호길이 |
| 지시선 (LEADER, MULTILEADER) | |

블록·해치는 아직 지원하지 않습니다. 선택에 섞여 있으면 조용히 빠지지 않고
`무시 N개 (Hatch 2, ...)` 로 보고합니다.

## 옵션

`CLIPDWGCFG` 로 편집하며 `%APPDATA%\clipdwg\settings.json` 에 저장됩니다.
파일을 직접 고쳐도 되고, 프로파일을 여러 개 두고 상황에 따라 바꿔 쓸 수 있습니다.

| 항목 | 설명 |
|---|---|
| 색상별 선두께 | ACI 번호(1~255) 또는 `#RRGGBB` → mm |
| 규칙 없는 색 두께 | 위 표에 없는 색에 쓸 기본 두께 |
| 최소 두께 | 두께 0(hairline)을 실제로 그릴 때의 굵기 |
| 도면 단위 1 = ? mm | 도면이 mm 단위면 1, m 단위면 1000 |
| 출력 축척 | 붙여넣기 크기 배율 |
| 여백 | 도형 바깥 여백(mm) |
| 흰색을 검정으로 | 흰 배경 문서용. 기본 켜짐 |
| 모든 선을 검정으로 | 흑백 출력용 |
| SHX 글꼴 대체 | SHX 글꼴을 어떤 TrueType 글꼴로 바꿔 그릴지 |

색 해석 순서는 **트루컬러 규칙 → ACI 규칙 → 기본값** 입니다. `ByLayer` / `ByBlock` 은
복사 시점에 실제 색으로 해석한 뒤 규칙을 찾습니다. 꺼진·동결된 레이어의 객체는 제외됩니다.

<details>
<summary>settings.json 예시</summary>

```json
{
  "activeProfile": "default",
  "profiles": [
    {
      "name": "default",
      "mmPerDrawingUnit": 1,
      "outputScale": 1,
      "marginMm": 1,
      "defaultWidthMm": 0.25,
      "minWidthMm": 0.05,
      "whiteToBlack": true,
      "forceBlack": false,
      "widths": [
        { "aci": 1, "mm": 0.13 },
        { "aci": 5, "mm": 0.25 },
        { "aci": 7, "mm": 0.35 },
        { "rgb": "#FF8000", "mm": 0.6 }
      ],
      "defaultShxFont": "Arial",
      "shxSubstitutes": [
        { "shx": "romans", "font": "Arial" },
        { "shx": "whgtxt", "font": "맑은 고딕" }
      ]
    }
  ]
}
```

</details>

## 알아 둘 점

**SHX 글꼴은 TrueType으로 대체됩니다.**
`whgtxt.shx` 같은 SHX는 윤곽선 글꼴이 아니라서 EMF에 글자로 넣을 수 없습니다. 기본값으로
한글 큰글꼴은 맑은 고딕, 영문 SHX는 Arial로 바꿔 그립니다. **자간이 원본과 다소 달라집니다.**
대체가 일어나면 어떤 SHX가 바뀌었는지 명령행에 알려 줍니다.

**치수는 붙여넣은 뒤 분리됩니다.**
AutoCAD가 분해한 결과를 쓰므로 화살촉 모양과 문자 위치는 치수 스타일 그대로 나오지만,
개별 선·화살촉·문자로 나뉘어 치수로서의 연동성은 없습니다.

**MText의 인라인 서식은 사라집니다.**
줄바꿈과 문단 배치는 정확하지만, 한 MText 안에서 색이나 글꼴을 바꾼 부분은 단일 스타일로
합쳐집니다.

**WCS XY 평면에 투영합니다.**
3D로 기울어진 원·호는 직선으로 근사하며, 몇 개가 그렇게 처리됐는지 명령행에 표시됩니다.
2D 도면에서는 해당 사항이 없습니다.

## 성능

EMF 렌더 구간 실측 (도형당 4개 선분 기준):

| 도형 수 | 렌더 시간 |
|---:|---:|
| 10,000 | 52 ms |
| 50,000 | 239 ms |
| 200,000 | 679 ms |

AutoCAD 세션에서 `CLIPDWG` 를 처음 실행할 때 좌표계 보정에 약 90ms가 한 번 더 듭니다.

## 개발

```
src/ClipDwg.Core/     AutoCAD 비의존 — IR, EMF 렌더러, 색상-두께 설정, 옵션창, 클립보드
src/ClipDwg/          AutoCAD 의존 — 명령 진입점, 엔티티 추출
test/ClipDwg.Tests/   ClipDwg.Core 만 참조하므로 AutoCAD 없이 실행
package/              자동 로드 번들 정의
tools/install.ps1     빌드 + 내 PC에 설치
tools/pack.ps1        빌드 + 배포용 dist\clipdwg-<버전>.zip 생성
```

```powershell
dotnet build ClipDwg.sln -c Release
dotnet test  ClipDwg.sln -c Release
```

클립보드 테스트는 실제 시스템 클립보드를 덮어씁니다. 빼려면
`--filter "Category!=Clipboard"`.

AutoCAD를 다른 경로에 설치했다면:

```powershell
dotnet build ClipDwg.sln -c Release -p:AcadDir2024="D:\...\AutoCAD 2024\"
```

### 릴리스

`package\PackageContents.xml` 의 `AppVersion` 을 올린 뒤:

```powershell
powershell -ExecutionPolicy Bypass -File tools\pack.ps1
gh release create v1.0.0 dist\clipdwg-1.0.0.zip --title "clipdwg 1.0.0" --notes "..."
```

zip 버전은 `AppVersion` 을 그대로 따라가므로 태그와 어긋나지 않게 맞춰 주세요.

### 설계 메모

**`ClipDwg.Core` 에 Autodesk 어셈블리를 참조하지 마세요.** 테스트가 AutoCAD 없이 도는 것도,
`DataContractJsonSerializer` 가 어셈블리 특성을 훑을 때 `acmgd` 를 끌어오지 않는 것도
(`ClipDwg` 에는 `[assembly: CommandClass]` 가 있습니다) 그 제약 덕분입니다.

**폴리라인의 bulge와 치수 배치는 직접 계산하지 않습니다.** AutoCAD의 세그먼트 API와
`Explode()` 결과를 씁니다. OCS→WCS 변환, 미러링(-Z 법선), 스플라인 피팅, 치수 스타일을
이미 정확히 처리해 주기 때문입니다.

**EMF 좌표계는 런타임에 실측 보정합니다.** GDI+가 프레임 변환에 쓰는 해상도와 기록용
`Graphics` 가 보고하는 DPI가 다르고, 거기에 디스플레이 배율과 프로세스의 DPI 인식 여부까지
얽힙니다. 계산으로 맞히는 대신 크기를 아는 시험용 메타파일을 만들어 되재는 방식을 씁니다
(`DeviceResolution`).

## AutoCAD 2025 이상

2025부터 관리형 API가 .NET 8로 넘어가 net48 DLL은 로드되지 않습니다. 두 프로젝트 모두
`AutoCAD 2025` 폴더가 있으면 `net8.0-windows` 타깃이 자동으로 켜지도록 되어 있으나,
아직 실제로 검증하지는 못했습니다.
