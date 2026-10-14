# Geuneda.Analytics - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.analytics`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core` 0.4.0(ModuleLogger, `IAppIdentity`, `AppEnv`, `IMonotonicClock`), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`. http·account 는 선택입니다(사용자 결정 2026-10-14: core 만 필수). 있으면 `versionDefines` 로 `Geuneda.Analytics.Collector`(define `GEUNEDA_ANALYTICS_HTTP`), `Geuneda.Analytics.Account`(define `GEUNEDA_ANALYTICS_ACCOUNT`)가 켜집니다. 이 define 은 각 asmdef 안에서만 정의되므로 다른 어셈블리 코드에서 `#if` 로 쓰지 않습니다
- **벤더 어댑터**: 결정 D3 에 따라 별도 패키지(`com.geuneda.analytics.firebase` 등). 이 패키지에 벤더 SDK 참조를 넣지 않습니다
- **출처**: Lucky Defense 의 Percent EventLogging(로드맵 4-1). 따른 것: sink 비트마스크 팬아웃, 실패한 sink 비트만 저장해 그 sink 로만 재전송, 기록 시점 공통 맥락 스냅샷(저장해도 유지), 설치마다 순번, 세션 이벤트. 보완한 것: 재전송 트리거(레퍼런스는 새 이벤트만 → 백오프·포그라운드 복귀·네트워크 복구·다음 실행), 자사 수집 배치(레퍼런스는 1건 1 POST), 4xx/5xx 규칙 명시(레퍼런스는 상태 0 만 재시도, 5xx 버림), 하드코딩 AES 키 제거(사용자 결정: 평문), 파일 수 초과 처리 교착 회피(잠금 없음, 메인 스레드), sink 별 큐(한 sink 의 지연이 다른 sink 를 막지 않음), 이벤트 ID 로 서버 중복 제거, 순번 예약(레퍼런스는 이벤트마다 PlayerPrefs 저장, 시간마다 리셋)

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/AnalyticsService.cs` | 공개 서비스. 검증, 맥락 스냅샷, sink 등록·사용자 정보 전파, 일시정지·종료·네트워크 복구 처리 |
| `Runtime/Internal/SinkChannel.cs` | sink 하나의 메모리 큐, 전송 루프(`RunAsync`), 백오프, 깨우기 타이머, 디스크로 옮기기(`Spill`, `Persist`) |
| `Runtime/Internal/EventFileStore.cs` | sink 폴더의 배치 파일(임시 파일 → 이름 바꾸기, 상한 초과 시 오래된 것부터 삭제, 손상 파일 삭제) |
| `Runtime/Internal/AnalyticsJson.cs` | 배치 JSON 읽기·쓰기(Newtonsoft 리더·라이터 직접 사용) |
| `Runtime/Internal/SequenceCounter.cs` | 순번과 PlayerPrefs 예약 저장 |
| `Runtime/Internal/AnalyticsLifecycle.cs` | 숨은 수명 컴포넌트(일시정지·복귀·종료, 1초마다 네트워크 상태) |
| `Runtime/Protocol/AnalyticsWire.cs` | 배치 형식 상수. `Server~` 가 링크하므로 Unity API 를 쓰지 않습니다 |
| `Runtime/Collector/` | `CollectorSink`(선택 어셈블리) |
| `Runtime/Account/` | `AppSessionAnalytics`(선택 어셈블리) |
| `Server~/` | ASP.NET Core 수집 서버(`src`), xUnit 테스트(`tests`), 샘플(`samples`) |

## 3. 규칙

- **스레드**: 서비스와 채널은 메인 스레드에서만 상태를 바꾸고 잠금이 없습니다. 공개 메서드는 만든 스레드가 아니면 `InvalidOperationException` 입니다. 파일 입출력도 메인 스레드에서 동기로 합니다(배치 하나는 작고, 일시정지 때는 동기로 써야 함). 스레드 풀로 옮기면 잠금이 필요해지고 레퍼런스의 교착(저장 잠금을 쥔 채 초과 처리가 같은 잠금을 기다림)을 다시 만들 수 있습니다.
- **전달 의미**: 저장 파일은 sink 결과가 나온 뒤에 지웁니다(적어도 한 번). 보내는 중인 메모리 배치는 일시정지·종료·`Dispose` 때 파일로 써 두고(`_inFlightFile`), 결과가 나오면 그 파일을 지우거나(받음·버림) 둡니다(재시도). 받음 뒤 삭제 전에 앱이 죽으면 중복 전송될 수 있고, 자사 수집 서버가 이벤트 ID 로 거릅니다. 파일을 읽자마자 지우는 방식(레퍼런스)으로 바꾸지 마세요.
- **sink 격리**: sink 는 바깥 코드(SDK 어댑터)입니다. `SendAsync` 예외는 Error 로그 후 그 배치를 버리고(같은 배치로 계속 예외가 나지 않게), `SetUserId`·`SetUserProperty` 예외는 Error 로그 후 다음 sink 로 계속합니다. 결과 코드로 바꾸는 것이 아니라 로그로 드러내는 격리입니다(호스트 `CLAUDE.md` 실패 처리 규칙의 `catch (Exception)` 금지는 결과로 숨기는 경우). `IsReady` 예외는 잡지 않습니다. `Close` 의 `_lifetime.Cancel()` 은 sink 가 토큰에 건 콜백의 예외를 `AggregateException` 으로 모아 던지므로 잡아서 Error 로 남깁니다(다른 채널의 저장·취소가 끝나야 함, 2026-10-14 리뷰).
- **메인 스레드 복귀**: sink 의 `SendAsync` 가 다른 스레드에서 끝나면(예외 포함) UniTask continuation 이 그 스레드에서 돕니다. `SendAsync` 래퍼는 catch 에서 예외만 보관하고, 만든 스레드가 아니면 `UniTask.SwitchToMainThread()` 한 뒤 로그와 결과 처리를 합니다. catch 안에서 sink 멤버(`Id` 포함)를 부르지 않습니다. 채널의 `Id` 는 등록할 때 읽어 둔 값입니다(2026-10-14 리뷰 1·2회차, `SinkFinishingOnAnotherThread_ContinuesOnTheMainThread`, `SinkFailingOnAnotherThread_IsHandledOnTheMainThread`).
- **백오프 둘**: sink 백오프(`_failures`)는 sink 가 `Retry` 를 돌려줬을 때 그 sink 로 보내는 것을 멈춥니다. 저장소 백오프(`_storeFailures`, `StoreFailed`)는 목록·파일 읽기·분할이 실패했을 때 저장된 배치만 건너뛰게 하고 메모리 큐는 계속 보냅니다(디스크 문제가 이어져도 새 이벤트는 나감). `StoreFailed` 는 백오프와 깨우기를 함께 예약하고(전송 루프 밖에서 알게 된 실패라 예약하지 않으면 멈춤, 2026-10-14 리뷰 1회차), 저장소 백오프 중에는 `TryTakeBatch` 가 매번 재시도 깨우기를 다시 예약합니다. 깨우기 타이머는 하나라 더 이른 깨우기(준비 확인 1초 등)가 오면 늦은 것이 취소되기 때문입니다(리뷰 2회차, `ListingRetry_IsNotLostBehindReadinessPolls`).
- **디스크 실패**: 쓰기가 실패하면 `DiskRetryDelay`(5초) 동안 상한 초과 때 쓰기를 시도하지 않고(일시정지·닫기의 `Persist` 는 그래도 씀), 메모리 큐가 `MaxQueuedEventsPerSink` 를 넘으면 오래된 것부터 버립니다(Warning 5초에 한 번). 메모리 상한이 디스크가 정상일 때만 지켜지지 않게 합니다.
- **저장 배치 크기**: 저장된 배치가 지금 `MaxBatchSize` 보다 크면(이전 실행의 설정) `EventFileStore.TrySplit` 으로 원본 자리(이름 순서)에 조각 파일을 모두 쓴 뒤 원본을 지우고 파일 수 상한(`TrimOverflow`)을 적용합니다. 조각을 쓰다 실패하면 쓴 조각을 지우고 원본을 둔 채 저장소 백오프합니다.
- **목록 읽기**: `EventFileStore.TryLoad` 는 `Directory.Exists` 로 판정하지 않고 `GetFiles` 를 바로 부릅니다. `DirectoryNotFoundException` 만 빈 목록(완료)이고 그 밖의 입출력·권한 오류는 완료로 표시하지 않습니다(`Exists` 는 권한 오류에도 false 라 빈 목록으로 굳음, 2026-10-14 리뷰 2회차). 남은 `.tmp` 는 하나씩 지우고, 지우지 못해도 목록은 그대로 씁니다. 쓰기는 목록 없이도 합니다(목록을 못 읽어도 새 배치는 저장). 그래서 목록을 합친 뒤에도 파일 수 상한을 적용합니다(리뷰 3회차, `RecoveredListing_AppliesTheStoredBatchLimit`).
- **파일 입출력 대역**: `EventFileStore` 는 `IBatchFileSystem`(기본 `SystemBatchFileSystem`)으로 입출력합니다. 서비스 내부 생성자의 마지막 인자로 바꿔 넣고, 테스트는 `FaultyFileSystem` 으로 목록·읽기·삭제 실패를 결정론적으로 만듭니다(OS 권한·속성에 기대는 테스트는 플랫폼마다 달라 쓰지 않음).
- **채널 루프**: 한 sink 에 전송은 하나만(`_running`). 재진입(`SendAsync` 안에서 `LogEvent`)은 큐에 넣고 같은 루프가 이어서 처리합니다. 한 번에 `BatchesPerFrame`(8)개 배치를 보낸 뒤 `UniTask.Yield` 로 한 프레임 양보합니다(동기로 끝나는 SDK sink 와 저장 배치가 많을 때 프레임을 붙잡지 않게). 저장 배치를 메모리 큐보다 먼저 보냅니다. 배치 대기(`BatchDelay`)는 메모리 큐에만 적용하고, 일시정지 중에는 무시합니다.
- **깨우기**: 채널에는 깨우기 타이머가 하나이고, 더 이른 시각의 요청만 기존 타이머를 바꿉니다. `WakeAsync` 는 자기 타이머일 때만 `_wake` 를 비웁니다. 백오프 중에는 새 이벤트가 전송을 시작하지 않습니다(레퍼런스는 새 이벤트마다 재전송을 시작해 장애 때 요청이 몰림). 백오프는 틱으로 계산하고 50~100% 무작위입니다.
- **복귀 판정**: `HandlePause(false)` 는 `true` 를 먼저 받았을 때만 복귀로 봅니다(servertime 과 같은 가드).
- **일시정지 중 기록**: 일시정지·종료 뒤 기록한 이벤트는 보내기를 시도한 뒤 남은 것을 바로 디스크에 씁니다. 여러 오브젝트의 `OnApplicationPause`·`OnApplicationQuit` 순서는 정해져 있지 않아, 다른 컴포넌트(예: account 의 세션 끝)가 이 서비스보다 늦게 기록해도 잃지 않게 하기 위한 것입니다.
- **파일 이름**: `<UTC 틱 19자리>-<카운터 6자리>.json`, 나눈 조각은 `<원본 이름>-<번호 3자리>.json`(원본 자리 바로 앞). 이름 순서가 저장 순서입니다. 시작할 때 남은 `.tmp` 는 지웁니다. 손상된 파일(JSON 오류, 버전 불일치, 잘못된 UTF-8)은 Warning 후 지우고, 읽기 디스크 오류는 파일을 두고 백오프합니다.
- **JSON**: 쓸 때 짝 없는 서로게이트는 U+FFFD 로 바꾸고(UTF-8 인코딩 실패로 저장이 깨지지 않게), 읽을 때는 잘못된 UTF-8 을 손상으로 봅니다. 날짜 해석을 끄고(`DateParseHandling.None`) 정수 토큰은 `long`, 실수 토큰은 `double` 입니다. `long` 범위를 넘는 정수(BigInteger)는 손상입니다. 와이어 형식을 바꾸면 `AnalyticsWire.Version` 을 올리고 서버·테스트(`Serialize_WritesTheWireFormat` 과 서버 `ClientBatch`)를 함께 바꿉니다.
- **표준 이벤트**(`Runtime/Standard/`): 이름·파라미터 이름은 Firebase 권장 이름이고 값은 바꾸지 않습니다(저장된 이벤트와 어댑터가 읽음). 기록은 확장 메서드(`IAnalyticsService` 에 멤버를 늘리지 않음), 읽기는 `TryRead`(필수 파라미터의 형식까지 확인, 실패하면 어댑터가 일반 이벤트로 보냄). 금액은 `double`(수집·저장 형식과 같음). 어댑터 패키지가 이 계약에 의존하므로 바꾸면 네 어댑터와 버전을 함께 올립니다.
- **값 형식**: `AnalyticsValue.FromObject` 는 enum 을 받지 않습니다(이름과 숫자 중 무엇을 보낼지 호출부가 정함). 받는 형식을 늘리면 저장·서버 형식이 그대로 왕복하는지 확인합니다.
- **순번**: 100개씩 예약하고 예약할 때만 `PlayerPrefs.Save` 합니다. 같은 번호가 다시 나오지 않는 것이 불변식입니다.
- **수명 컴포넌트**: 플레이 중에만 만들고 `HideInHierarchy` 만 붙입니다(servertime 과 같은 이유). `Dispose` 는 파괴 전에 `Unbind` 합니다.
- **static 가변 상태 금지**(D2). 로거만 static 입니다.
- **자사 수집 분할**: `CollectorSink` 는 본문이 `MaxBodyBytes` 를 넘거나 413 을 받으면 배치를 반으로 나눠 차례로 보내고 이벤트 하나면 버립니다. 나눈 조각 하나가 재시도면 전체를 재시도로 돌려줍니다(받은 조각은 서버가 ID 로 거름).
- **서버**: 배치 형식 오류는 400, 크기 초과 413, JSON 아님 415, 이벤트별 오류는 200 의 `rejected` 입니다. 숫자는 원문에 `.`·`e`·`E` 가 없으면 정수로 보고 `long` 범위를 넘으면 그 이벤트를 거절합니다(클라이언트 `AnalyticsJson` 과 같은 규칙, double 로 반올림하지 않음). 저장소 예외는 잡지 않습니다(500 → 클라이언트 재전송). 짝 없는 서로게이트 이스케이프는 `GetString` 이 `InvalidOperationException` 을 던지므로 이벤트 단위로 잡아 거절합니다. 서비스 매개변수는 `[FromServices]` 로 받습니다(POST 에서 등록 안 된 형식을 본문으로 추론하지 않게).

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.Analytics.Tests.EditMode`): 값 변환, JSON 왕복·형식·손상, 서비스(맥락, 팬아웃, 오용, 준비 대기, 재시도·백오프, 버림, sink 예외, 배치 대기, 일시정지 저장·다음 실행, 보내는 중 배치 저장, 복귀, 네트워크 복구, 큐·저장 상한, 손상 파일, Dispose, 순번, 프레임당 배치 수, 읽기 실패 뒤 스스로 재시도(파일 자리를 폴더로 바꿈), 지우지 못하는 임시 파일·목록 실패(`FaultyFileSystem`), 저장소 백오프 중 새 이벤트 전송과 준비 확인에 밀리지 않는 재시도, 작업자 스레드 완료·예외 뒤 메인 스레드 복귀, 큰 저장 배치 분할과 분할 후 파일 상한, 취소 콜백 예외 격리, 디스크 실패 때 메모리 상한(sink 폴더 자리에 파일)). 결정론을 위해 내부 생성자로 가짜 단조 시계, 수동 지연(`ManualDelay`), 메모리 순번 저장소를 넣습니다.
- `Tests/EditMode/Collector/`: `CollectorSink` 상태 코드 처리, 요청 형식, 서비스와 함께 503 저장 후 재전송. `SampleServerInteropTest` 는 Explicit(샘플 서버와 `GEUNEDA_ANALYTICS_INTEROP_URL` 필요).
- `Tests/EditMode/Account/`: 실제 `AppSession` 으로 세션 ID 와 시작·끝 이벤트 순서.
- `Tests/PlayMode/`: 숨은 수명 컴포넌트에 실제 Unity 메시지(`SendMessage`)로 일시정지·종료와 프레임 양보.
- 서버: `dotnet test "Server~/Geuneda.Analytics.Server.slnx"`.
- http·account 가 없는 프로젝트에서 선택 어셈블리가 빠지고 컴파일되는지는 core·analytics 만 넣은 임시 프로젝트로 확인했습니다(2026-10-14). 선택 어셈블리의 참조나 `versionDefines` 를 바꾸면 다시 확인합니다.

## 5. 문서

공개 API 를 바꾸면 `README.md`, `CHANGELOG.md`, 호스트 `claude-skills/geuneda-analytics/` 스킬을 함께 고칩니다.
