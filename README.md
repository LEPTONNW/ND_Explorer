# ND Explorer

ND Explorer는 파일 탐색, 이미지·동영상 확인, 파일 관리와 저장 공간 분석을 하나의 화면에서 제공하는 Windows용 WPF 파일 관리자입니다.

현재 버전은 개발 중인 프리뷰이며 Windows 10/11 x64 환경을 대상으로 합니다. 보호된 파일과 폴더도 안정적으로 분석할 수 있도록 실행 시 UAC 관리자 권한을 요청합니다.

## 주요 기능

- 비동기 파일·폴더 탐색과 실시간 파일 시스템 변경 반영
- 뒤로, 앞으로, 상위 폴더, 새로고침 및 주소 직접 이동
- `Backspace`, `Alt+←/→`, 마우스 뒤로·앞으로 버튼 지원
- 파일명 첫 문자와 접두어를 이용한 빠른 항목 선택
- 이름, 수정 날짜, 유형, 크기 정렬과 이름 필터
- 복사, 잘라내기, 붙여넣기, 이름 변경, 휴지통 삭제와 영구 삭제
- 우측 이미지 미리보기와 독립 이미지 뷰어
- 확대·축소, 화면 맞춤, 드래그 이동과 전체 화면 이미지 보기
- LibVLC 기반 내장 동영상 플레이어
- 재생·정지, 반복 재생, 탐색, 볼륨, 전체 화면과 키보드 제어
- 드라이브별 저장 공간 분석과 계층형 트리맵
- 분석 도중 이동·삭제되는 파일에 대한 예외 격리와 자동 재분석
- 앱 내부 라이브러리 라이선스 고지 화면

자세한 설계와 구현 현황은 [프로젝트 마스터 문서](masterchunk1.md)에서 확인할 수 있습니다.

## 요구 사항

- Windows 10 또는 Windows 11 x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022를 사용할 경우 .NET 데스크톱 개발 워크로드

## 빌드

```powershell
dotnet restore .\ND_Explorer.slnx
dotnet build .\ND_Explorer.slnx -c Release --no-restore
```

빌드된 실행 파일은 다음 위치에 생성됩니다.

```text
ND_Explorer\bin\Release\net9.0-windows\ND_Explorer.exe
```

## 주요 조작

| 입력 | 동작 |
|---|---|
| `Backspace` / `Alt+←` / 마우스 뒤로 | 이전 경로 |
| `Alt+→` / 마우스 앞으로 | 다음 경로 |
| `Alt+↑` | 상위 폴더 |
| `F5` | 현재 폴더 새로고침 |
| 문자 입력 | 해당 문자 또는 접두어로 시작하는 항목 선택 |
| `Enter` | 선택한 파일 또는 폴더 열기 |
| `F2` | 이름 변경 |
| `Delete` | 휴지통으로 이동 |
| `Shift+Delete` | 영구 삭제 |

전체 단축키는 프로젝트 마스터 문서의 [이미지 뷰어](masterchunk1.md#6-이미지-미리보기-및-이미지-뷰어)와 [동영상 플레이어](masterchunk1.md#7-vlc-기반-동영상-플레이어) 항목을 참고하세요.

## 기술 구성

- C# / WPF
- .NET 9 (`net9.0-windows`)
- x64
- LibVLCSharp.WPF 3.10.1
- VideoLAN.LibVLC.Windows 3.0.23.1

## 라이선스

ND Explorer 자체 소스 코드는 [Apache License 2.0](LICENSE)으로 배포됩니다. 저작권 및 귀속 고지는 [NOTICE](NOTICE)를 참고하세요.

### 외부 구성요소

동영상 재생에는 VideoLAN 및 LibVLCSharp 기여자들이 개발한 LGPL 2.1 이상 라이선스 구성요소를 사용합니다. GPL 플러그인이 추가된 `VideoLAN.LibVLC.Windows.GPL` 패키지는 포함하지 않습니다.

배포 조건과 저작권 고지는 다음 문서에 포함되어 있습니다.

- [Third-party notices](ND_Explorer/Licenses/THIRD-PARTY-NOTICES.txt)
- [GNU LGPL 2.1 전문](ND_Explorer/Licenses/LGPL-2.1.txt)
