# clipdwg

AutoCAD에서 선택한 **선·호·원·폴리라인·텍스트·치수·지시선**을 벡터 그래픽(EMF)으로
클립보드에 복사합니다.
Word·PowerPoint·한글에 붙여넣으면 확대해도 깨지지 않고, 그룹해제해서 편집할 수 있습니다.
**색상별로 선두께를 지정**할 수 있는 것이 기본 Ctrl+C와의 가장 큰 차이입니다.

## 설치

```powershell
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

`%APPDATA%\Autodesk\ApplicationPlugins\clipdwg.bundle` 로 설치됩니다. AutoCAD를 다시 켜면
명령을 바로 쓸 수 있습니다. 제거는 `tools\install.ps1 -Uninstall`.

개발 중에는 번들 설치 없이 `NETLOAD` 로 `src\ClipDwg\bin\Release\net48\ClipDwg.dll` 을 직접
읽어도 됩니다. (같은 폴더의 `ClipDwg.Core.dll` 이 함께 있어야 합니다.)

## 명령

| 명령 | 하는 일 |
|---|---|
| `CLIPDWG` | 선택한 객체를 EMF로 클립보드에 복사 |
| `CLIPDWGCFG` | 색상별 선두께·글꼴·축척 옵션 편집 |

객체를 먼저 골라 놓고 `CLIPDWG` 를 쳐도 되고, 명령을 먼저 친 뒤 골라도 됩니다.

## 옵션

`CLIPDWGCFG` 로 편집하며 `%APPDATA%\clipdwg\settings.json` 에 저장됩니다. 파일을 직접 고쳐도
됩니다. 프로파일을 여러 개 두고 상황에 따라 바꿔 쓸 수 있습니다.

| 항목 | 설명 |
|---|---|
| 색상별 선두께 | ACI 번호(1~255) 또는 `#RRGGBB` → mm. 트루컬러 규칙이 ACI 규칙보다 우선 |
| 규칙 없는 색 두께 | 위 표에 없는 색에 쓸 기본 두께 |
| 최소 두께 | 두께 0(hairline)을 실제로 그릴 때의 굵기 |
| 도면 단위 1 = ? mm | 도면이 mm 단위면 1, m 단위면 1000 |
| 출력 축척 | 붙여넣기 크기 배율 |
| 여백 | 도형 바깥 여백(mm). 가장 굵은 선의 절반은 자동으로 더해집니다 |
| 흰색을 검정으로 | 흰 배경 문서에 붙일 때 사실상 필수. 기본 켜짐 |
| 모든 선을 검정으로 | 흑백 출력용 |
| SHX 글꼴 대체 | SHX 글꼴을 어떤 TrueType 글꼴로 바꿔 그릴지 |

`ByLayer` / `ByBlock` 색은 복사 시점에 실제 색으로 해석한 뒤 규칙을 찾습니다.
꺼진·동결된 레이어의 객체는 제외되고, 몇 개가 빠졌는지 명령행에 표시됩니다.

## 알아 둘 점

**SHX 글꼴은 TrueType으로 대체됩니다.** `whgtxt.shx` 같은 SHX는 윤곽선 글꼴이 아니라서 EMF에
글자로 넣을 수 없습니다. 기본값으로 한글 큰글꼴은 맑은 고딕, 영문 SHX는 Arial로 바꿔 그리며,
`CLIPDWGCFG` 의 [SHX 글꼴 대체] 탭에서 조정할 수 있습니다. **자간이 원본과 다소 달라집니다.**
대체가 일어나면 명령행에 어떤 SHX가 바뀌었는지 알려 줍니다.

**텍스트는 글자로 남습니다.** 윤곽선으로 분해하지 않고 EMF의 텍스트 레코드로 넣기 때문에
붙여넣은 뒤에도 선택·편집·검색이 됩니다.

**MText의 인라인 서식은 사라집니다.** 줄바꿈과 문단 배치는 AutoCAD의 분해 결과를 그대로 쓰므로
정확하지만, 한 MText 안에서 색이나 글꼴을 바꾼 부분은 단일 스타일로 합쳐집니다.

**치수는 AutoCAD가 분해한 결과를 씁니다.** 선형·정렬·반지름·지름·각도·좌표·호길이 모두
지원하며, 화살촉 모양과 문자 위치는 치수 스타일을 그대로 따릅니다. 화면에 보이는 그대로가
나오는 대신, 붙여넣은 뒤에는 개별 선·화살촉·문자로 분리되어 치수로서의 연동성은 없습니다.
지시선(LEADER)과 다중지시선(MULTILEADER)도 같은 방식으로 처리됩니다.

**지원 범위 밖의 객체는 조용히 빠지지 않습니다.** 블록·해치 등이 선택에 섞여 있으면
"무시 N개 (BlockReference 3, Hatch 1)" 처럼 명령행에 보고합니다.

**WCS XY 평면에 투영합니다.** 3D로 기울어진 원·호는 직선으로 근사하며, 몇 개가 그렇게
처리됐는지 명령행에 표시됩니다. 2D 도면에서는 해당 사항이 없습니다.

## 성능

EMF 렌더 구간 실측 (도형당 4개 선분 기준):

| 도형 수 | 렌더 시간 |
|---:|---:|
| 10,000 | 52 ms |
| 50,000 | 239 ms |
| 200,000 | 679 ms |

AutoCAD 세션에서 `CLIPDWG` 를 처음 실행할 때 좌표계 보정에 약 90ms가 한 번 더 듭니다.

## 구조

```
src/ClipDwg.Core/     AutoCAD 비의존 — IR, EMF 렌더러, 색상-두께 설정, 옵션창, 클립보드
src/ClipDwg/          AutoCAD 의존 — 명령 진입점, 엔티티 추출
test/ClipDwg.Tests/   ClipDwg.Core 만 참조하므로 AutoCAD 없이 실행 가능
```

`ClipDwg.Core` 에 Autodesk 어셈블리를 참조하지 마세요. 테스트가 AutoCAD 없이 도는 것도,
`DataContractJsonSerializer` 가 어셈블리 특성을 훑을 때 `acmgd` 를 끌어오지 않는 것도
그 제약 덕분입니다.

```powershell
dotnet test ClipDwg.sln -c Release
```

클립보드 테스트는 실제 시스템 클립보드를 덮어씁니다. 빼려면:

```powershell
dotnet test ClipDwg.sln -c Release --filter "Category!=Clipboard"
```

## AutoCAD 2025 이상

2025부터 관리형 API가 .NET 8로 넘어가 net48 DLL은 로드되지 않습니다. 두 프로젝트 모두
`AutoCAD 2025` 폴더가 있으면 `net8.0-windows` 타깃이 자동으로 켜지도록 되어 있습니다.
"# clipdwg" 
