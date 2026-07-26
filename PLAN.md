# clipdwg — AutoCAD 선택 객체를 벡터로 클립보드 복사

## 1. 확정 사항

| 항목 | 결정 |
|---|---|
| 대상 | AutoCAD 2024 (R24.3, `acmgd/acdbmgd/accoremgd` 24.3.171) |
| 언어/런타임 | C# / .NET Framework 4.8 (AutoCAD 2024 관리형 API 요구 버전) |
| 출력 포맷 | EMF — `CF_ENHMETAFILE` (Office·한글 붙여넣기 후 그룹해제 편집 가능) |
| 명령 | `CLIPDWG` (복사), `CLIPDWGCFG` (옵션 대화상자) |
| 옵션 저장 | `%APPDATA%\clipdwg\settings.json`, 프로파일 다중 지원 |
| 대상 엔티티 | Line, Arc, Circle, LWPolyline / Polyline(2d·3d), DBText, MText |

빌드 환경은 이미 갖춰져 있음: VS 2022 Community + Build Tools, .NET Framework 4.8 타게팅 팩 확인.

## 2. 아키텍처

```
clipdwg/
  ClipDwg.sln
  src/ClipDwg/
    ClipDwg.csproj            net48, acmgd/acdbmgd/accoremgd 참조(CopyLocal=false)
    Commands.cs               CLIPDWG / CLIPDWGCFG 진입점
    Extract/
      Ir.cs                   중간표현: IrPolyline, IrArc, IrEllipse, IrText, IrStyle
      EntityExtractor.cs      DB 엔티티 → IR (타입 스위치, 리플렉션 없음)
      BulgeMath.cs            LWPolyline bulge → 원호 파라미터
    Render/
      EmfRenderer.cs          IR → GDI+ Metafile(EmfOnly)
      PenCache.cs             (색, 두께) 조합별 Pen 재사용
    Style/
      ColorWeightMap.cs       ACI 1~255 / TrueColor / ByLayer → mm
      Settings.cs             JSON 로드·저장, 기본 프로파일
    Clipboard/
      ClipboardWriter.cs      P/Invoke Open/Empty/SetClipboardData
    Ui/
      OptionsForm.cs          색상↔두께 표 (WinForms DataGridView)
  test/
    ClipDwg.Tests/            IR·bulge·색매핑 순수 로직 단위테스트 (AutoCAD 불필요)
  samples/
    sample.dwg                수동 회귀 테스트용
```

### 파이프라인
```
선택집합 → [Extract] IR 리스트 → [Style] 색→두께 해석 → [Render] EMF → [Clipboard] CF_ENHMETAFILE
```
IR을 중간에 두는 이유: 추출 로직과 렌더 로직 분리 → SVG 백엔드를 나중에 붙일 때 `Render/SvgRenderer.cs` 하나만 추가하면 됨. 단위테스트도 IR 단계에서 AutoCAD 없이 가능.

## 3. 핵심 구현 결정

**사전선택 픽셋 지원** — `[CommandMethod("CLIPDWG", CommandFlags.UsePickSet | CommandFlags.Redraw)]`.
"객체를 선택하면" 이라는 요구대로, 먼저 골라놓고 명령 치는 흐름과 명령 후 선택하는 흐름 둘 다 처리.

**좌표계** — 기본은 WCS XY 평면 투영. 3D 뷰에서 쓸 경우를 위해 `PROJECT=VIEW` 옵션으로 현재 뷰포트 DCS 투영을 추가(옵션, 2D 도면에선 결과 동일).

**단위·스케일** — `Metafile(hdc, frameRect, MetafileFrameUnit.Millimeter, EmfType.EmfOnly)`로 물리 크기를 가진 EMF 생성. `settings.json`의 `mmPerDrawingUnit`(기본 1.0)과 `outputScale`로 붙여넣기 실측 크기를 제어. 이렇게 해야 "선두께 0.25mm"가 진짜 0.25mm로 나옴.

**EmfOnly (EmfPlus 아님)** — EMF+는 Office가 그룹해제해서 편집할 때 깨지는 경우가 있음. 순수 EMF 레코드로 생성.

**클립보드 핸들 소유권** — .NET `Clipboard.SetData`의 EMF 경로는 신뢰할 수 없어 P/Invoke 직접 사용:
`Metafile.GetHenhmetafile()` → `OpenClipboard` → `EmptyClipboard` → `SetClipboardData(CF_ENHMETAFILE=14, hEmf)` → `CloseClipboard`.
성공 시 핸들 소유권이 시스템으로 넘어가므로 **`DeleteEnhMetaFile` 호출 금지**, 실패 시엔 반드시 해제. 여기가 리크/크래시 단골 지점이라 예외 경로까지 명시적으로 처리.

**색→두께 매핑 규칙** (우선순위 순)
1. 엔티티 TrueColor가 매핑표에 있으면 그 값
2. 엔티티 ACI 인덱스 매핑값
3. ByLayer → 레이어 색상으로 다시 1~2 조회
4. ByBlock → 컨테이너 색상 (최상위면 기본값)
5. 미정의 → `defaultWeight`
`weight = 0`은 "가장 얇은 선(hairline)"으로 처리.

**선 색상 자체** — 두께만 바꾸고 색은 원본 유지가 기본. 옵션으로 `forceBlack`(전부 검정 출력), `whiteToBlack`(ACI 7 흰색 → 검정, 흰 배경 문서용 — 실무에서 거의 항상 필요) 제공.

**성능** — 트랜잭션 하나로 전체 열고, (색,두께) 키로 `GraphicsPath`에 누적한 뒤 펜 그룹별로 한 번씩 `DrawPath`. 상태 전환 횟수를 색상 종류 수(보통 10 미만)로 줄임. 수만 개 엔티티도 체감 즉시.

## 4. 단계별 진행

| 단계 | 내용 | 완료 기준 |
|---|---|---|
| 1 | 솔루션 스캐폴딩, csproj, `CLIPDWG` stub | `NETLOAD` 후 명령 실행 → "n개 선택됨" 출력 |
| 2 | IR 정의 + Line/Arc/Circle 추출 | 단위테스트 통과, 콘솔에 IR 덤프 |
| 3 | LWPolyline(bulge 포함) / Polyline2d / Polyline3d | bulge→원호 변환 테스트 통과 |
| 4 | EMF 렌더 + 클립보드 | Word에 붙여넣기 → 벡터로 확대해도 안 깨짐, 그룹해제 편집됨 |
| 5 | 색→두께 매핑 + JSON 설정 | 색별 두께가 붙여넣기 결과에 반영 |
| 6 | `CLIPDWGCFG` 대화상자 | 표에서 편집·저장·프로파일 전환 |
| 7 | DBText / MText | 폰트·높이·회전·정렬·기울기·폭계수 반영 |
| 8 | 마무리 | `.bundle` 패키징(자동 로드), 대량 선택 성능 측정, README |

4단계까지가 "쓸 수 있는 최소 제품". 거기서 한 번 실제로 붙여넣어 보고 나머지 진행하는 걸 권합니다.

## 5. 미리 짚어둘 위험 요소

**SHX 폰트 텍스트 (7단계 최대 난관)** — 도면 텍스트 스타일이 `romans.shx`, 한글 `whgtxt.shx` 같은 SHX면 EMF에 넣을 TTF가 없습니다. 대응 세 가지:
- (a) SHX→TTF 대체 매핑표를 설정에 두기 (예: `whgtxt.shx` → 맑은 고딕). 구현 쉬움, 자간이 원본과 다름
- (b) `Entity.Explode()`로 텍스트를 지오메트리로 분해해 폴리라인으로 그리기. 모양 정확, 파일 커짐, 붙여넣은 뒤 글자 편집 불가
- (c) 둘 다 제공하고 옵션 선택
→ 7단계 진입 시점에 실제 도면 폰트 확인하고 정하는 게 맞습니다. TTF 스타일이면 이 문제 자체가 없음.

**MText 인라인 서식** — 색 변경, 스택 분수, 필드, 단락 정렬 코드가 섞이면 완벽 재현이 어렵습니다. 1차 구현은 서식코드 제거 후 단일 스타일 렌더 + 미지원 서식 발견 시 경고 출력으로 갑니다.

**AutoCAD 2025 이상 대응** — 2025부터 관리형 API가 .NET 8로 넘어가 net48 DLL은 로드되지 않습니다. csproj를 처음부터 멀티타깃(`net48;net8.0-windows`)으로 잡아두면 나중에 조건부 컴파일 몇 줄로 끝납니다. 지금 비용이 거의 없으니 1단계에서 반영하겠습니다.

**Block Reference / Hatch / Dimension** — 요청 범위 밖이라 제외합니다. 선택집합에 섞여 있으면 조용히 무시하지 말고 "무시된 객체 N개(BlockReference 3, Hatch 1)"로 명령행에 보고하도록 하겠습니다.

## 6. 구현하면서 계획과 달라진 것

**어셈블리를 둘로 나눔 (`ClipDwg.Core` + `ClipDwg`)** — 원래는 단일 프로젝트 계획이었습니다.
`DataContractJsonSerializer`가 직렬화 전에 어셈블리 수준 특성을 전부 훑는데, `ClipDwg`에는
`[assembly: CommandClass]`가 있어서 그 과정에서 `acmgd`를 로드하려다 AutoCAD 밖에서는
실패했습니다. AutoCAD 비의존 코드를 `ClipDwg.Core`로 분리해서 해결했고, 덕분에 테스트가
AutoCAD 설치와 완전히 무관해졌습니다.

**bulge 수학은 직접 짜지 않음** — LWPolyline은 `GetSegmentType`/`GetArcSegmentAt`/
`GetLineSegmentAt`로, Polyline2d·3d는 `Explode()`로 처리했습니다. AutoCAD가 OCS→WCS 변환과
미러링(-Z 법선), 스플라인 피팅까지 이미 정확히 해 주기 때문에, 직접 구현하면 버그만 늘어납니다.
그래서 계획에 있던 `BulgeMath.cs`와 그 단위테스트는 없습니다.

**EMF 좌표계를 런타임에 실측 보정함 (`DeviceResolution`)** — 가장 오래 걸린 부분입니다.
GDI+는 mm 프레임을 장치 단위로 바꿀 때 물리 DPI(이 장비 114.7)를 쓰는데 기록용 `Graphics`는
자기 DPI를 96으로 보고하고, 거기에 디스플레이 배율(125%)까지 겹쳐서 내용이 프레임의 66%
크기로 그려졌습니다. 이 배율은 프로세스의 DPI 인식 여부에 좌우돼서 테스트 프로세스와 AutoCAD가
서로 다릅니다. 계산으로 맞히는 걸 포기하고, 크기를 아는 시험용 메타파일을 만들어 되재는 방식으로
바꿨습니다. 최초 1회 약 90ms, 이후 캐시.

**원호를 `AddArc` 대신 베지에로 넣음** — 메타파일의 장치 픽셀이 정사각형이 아닐 수 있는데
(모니터 가로/세로 물리 해상도가 다름) 그때 `AddArc`의 각도는 실제 각이 아니라 타원의
매개변수각으로 해석되어 시작·끝점이 어긋납니다. 베지에 제어점은 아핀변환을 정확히 따라갑니다.

**SHX는 (a) TTF 대체 방식으로 결정** — 계획 5절에서 열어 뒀던 선택지입니다. `Entity.Explode()`가
DBText에는 적용되지 않아 (b) 지오메트리 분해는 Express Tools 없이는 불가능했습니다. 대신
설정에서 편집 가능한 대체표를 두고, 대체가 일어나면 명령행에 알리도록 했습니다.

## 7. 나중에 붙일 수 있는 것 (지금은 안 함)

- SVG 백엔드 — `Render/SvgRenderer.cs` 추가 + 클립보드에 `image/svg+xml` 커스텀 포맷 동시 등록
- 기존 `.ctb` 플롯 스타일 임포트 — 색↔두께를 이미 쓰는 설정 그대로 가져오기 (CTB 바이너리 파싱 필요)
- 블록 재귀 전개, 해치 채우기
