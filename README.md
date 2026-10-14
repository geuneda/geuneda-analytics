# Geuneda Analytics

분석 이벤트 로깅 패키지입니다. 이벤트를 기록한 순간의 공통 맥락(시각, 순번, 세션, 사용자, 앱)과 함께 만들어, 비트로 고른 sink(콘솔, 자사 수집 서버, Firebase·Airbridge·AppsFlyer·Facebook 어댑터)마다 따로 큐에 넣고 보냅니다. 한 sink 가 실패하면 그 sink 의 배치만 디스크에 저장해 그 sink 로만 다시 보내므로, 다른 sink 로 중복 전송되지 않습니다.

| 기능 | 타입 |
|------|------|
| 서비스 | `IAnalyticsService`, `AnalyticsService`, `AnalyticsOptions` |
| 이벤트 | `AnalyticsEvent`, `AnalyticsParameter`, `AnalyticsValue`, `AnalyticsAppInfo` |
| sink | `AnalyticsSinks`(비트), `IAnalyticsSink`, `AnalyticsSendResult`, `ConsoleSink` |
| 맥락 | `IAnalyticsClock`(시각), `IAnalyticsSessionSource`(세션 ID) |
| 자사 수집(http 가 있을 때) | `Geuneda.Analytics.Collector.CollectorSink`, `CollectorSinkOptions` |
| 세션 연동(account 가 있을 때) | `Geuneda.Analytics.Account.AppSessionAnalytics` |
| 표준 이벤트 | `AnalyticsStandardEvents`, `AnalyticsStandardParameters`, `AnalyticsPurchase`, `AnalyticsAdImpression`, `LogPurchase`·`LogAdImpression` 확장 메서드 |
| 배치 형식 | `AnalyticsWire`(저장 파일과 수집 요청이 같은 JSON) |
| 수집 서버(`Server~`) | `AddGeunedaAnalytics`, `MapGeunedaAnalytics`, `IAnalyticsEventStore`, `InMemoryAnalyticsEventStore`, `CollectedEvent` |

네임스페이스는 `Geuneda.Analytics` 입니다. 선택 어셈블리는 `Geuneda.Analytics.Collector`, `Geuneda.Analytics.Account` 네임스페이스를 씁니다.

## 설치

`Packages/manifest.json` 에 의존 패키지와 함께 추가합니다. Geuneda 패키지와 UniTask 는 git 의존성이라 함께 넣어야 합니다. Newtonsoft Json 은 Unity 레지스트리에서 자동으로 설치됩니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.analytics": "https://github.com/geuneda/geuneda-analytics.git"
  }
}
```

필수 의존성은 core 뿐입니다. 다음 패키지가 프로젝트에 있으면 해당 어셈블리가 자동으로 컴파일됩니다(asmdef `versionDefines`).

| 함께 설치한 패키지 | 켜지는 어셈블리 | 내용 |
|---|---|---|
| `com.geuneda.http` 0.1.0 이상 | `Geuneda.Analytics.Collector` | 자사 수집 서버로 배치 POST 하는 `CollectorSink` |
| `com.geuneda.account` 0.1.0 이상 | `Geuneda.Analytics.Account` | 세션 ID 와 세션 이벤트(`AppSessionAnalytics`) |

벤더 SDK 어댑터는 SDK 를 쓰는 게임만 설치하도록 별도 패키지입니다(결정 D3). 각 README 에 설치와 변환 규칙이 있습니다.

| 패키지 | sink | 준비 | 표준 이벤트 변환 |
|---|---|---|---|
| `com.geuneda.analytics.firebase` | `FirebaseAnalyticsSink` | `MarkReady()` | `purchase`(상품 목록), `ad_impression` |
| `com.geuneda.analytics.airbridge` | `AirbridgeAnalyticsSink` | 항상 | 주문 완료, 광고 노출(AppLovin 파트너 상세) |
| `com.geuneda.analytics.appsflyer` | `AppsFlyerAnalyticsSink` | `MarkReady()` | `af_purchase`, `logAdRevenue` |
| `com.geuneda.analytics.facebook` | `FacebookAnalyticsSink` | `FB.IsInitialized` | `FB.LogPurchase`, `AdImpression` |

어댑터 패키지는 SDK 가 없으면 컴파일되지 않으므로 SDK 를 쓰는 게임만 설치합니다. define `GEUNEDA_SKIP_VENDOR_ADAPTERS` 가 있으면 어댑터를 컴파일하지 않습니다(SDK 가 없는 geuneda-frameworks 호스트용).

## 사용 예

```csharp
using Geuneda.Account;
using Geuneda.Analytics;
using Geuneda.Analytics.Account;
using Geuneda.Analytics.Collector;
using Geuneda.Core;
using Geuneda.Http;
using Geuneda.Services;

/// <summary>분석 서비스와 세션 연동을 만들고 소유합니다. 정리할 때 세션 구독을 먼저 끊고 서비스를 닫습니다.</summary>
public sealed class AnalyticsBootstrap : IDisposable
{
    private readonly AppSessionAnalytics _session;

    public AnalyticsBootstrap(IAppIdentity identity, IAppSession appSession, ITimeService time)
    {
        _session = new AppSessionAnalytics(appSession);
        var analytics = new AnalyticsService(new AnalyticsOptions
        {
            Identity = identity,                              // 설치 ID
            Session = _session,                               // 세션 ID
            Clock = new TimeServiceAnalyticsClock(time),      // 서버 시각(아래 '시각')
            DefaultSinks = AnalyticsSinks.All
        });

        // sink 는 부트스트랩에서 먼저 등록합니다. SDK 초기화가 늦으면 sink 의 IsReady 가 false 인 동안 이벤트가 기다립니다
        if (!AppEnv.IsProd)
        {
            analytics.AddSink(new ConsoleSink());
        }

        var collectorHttp = new HttpClientBuilder().WithBaseAddress("https://log.mygame.com/").Build();
        analytics.AddSink(new CollectorSink(collectorHttp, new CollectorSinkOptions { Endpoint = "analytics/events" }));

        // 세션 시작·일시정지·재개·끝 이벤트는 자사 수집으로만(Firebase 는 session_start 를 예약해 둠)
        _session.LogSessionEvents(analytics, AnalyticsSinks.Collector);

        MainInstaller.Bind<IAnalyticsService>(analytics);
    }

    public void Dispose()
    {
        _session.Dispose();                                // 세션 이벤트 구독을 먼저 끊고
        MainInstaller.CleanDispose<IAnalyticsService>();   // 메모리 이벤트 저장, 요청 취소, 바인딩 제거
    }
}

// 호출부는 SDK 를 모릅니다
analytics.SetUserId(accountId);
analytics.SetUserProperty("tier", "gold");
analytics.LogEvent("stage_clear", new Dictionary<string, object> { { "stage", 3 }, { "result", "win" } });
analytics.LogEvent("ad_reward", parameters, AnalyticsSinks.Collector | AnalyticsSinks.Airbridge);   // 받을 곳을 고름
```

## 이벤트

- `LogEvent(name, parameters)` 는 `DefaultSinks` 로, `LogEvent(name, parameters, sinks)` 는 고른 sink 중 등록된 것으로 보냅니다. 등록되지 않은 비트는 무시합니다. 이벤트 이름이 비었거나 `sinks` 가 `None` 이면 예외입니다.
- 파라미터 값은 문자열(null 은 빈 문자열), 불리언, 정수형(`ulong` 은 `long` 범위까지), `float`·`double`·`decimal`(실수, NaN·무한대 불가)만 받습니다. enum 이나 그 밖의 형식은 `ArgumentException` 이니 호출부에서 문자열이나 숫자로 바꿔 넘깁니다. 값은 `AnalyticsValue` 로 바뀌어 저장·재전송해도 형식이 그대로 남습니다(정수는 정수, 실수는 실수).
- 이름 규칙(길이, 접두사, 문자)은 sink 마다 다르므로 각 어댑터가 맞춥니다. 자사 수집 서버의 기본 상한은 이름 100자, 파라미터 100개, 문자열 1000자입니다.
- 메인 스레드에서 씁니다. 다른 스레드에서 부르면 `InvalidOperationException` 입니다. SDK 콜백에서 기록할 때는 core 의 `MainThreadDispatcher.Post` 로 넘깁니다.
- `IAnalyticsService` 는 `IDisposable` 이라 `MainInstaller.CleanDispose<IAnalyticsService>()` 로 닫을 수 있습니다. 세션 연동(`AppSessionAnalytics`)은 그보다 먼저 `Dispose` 합니다.

이벤트마다 붙는 공통 맥락은 기록한 순간의 값이고, 저장했다가 다시 보낼 때도 그대로입니다.

| 필드 | 출처 |
|---|---|
| `Id` | 이벤트 ID(GUID 32자리). 수집 서버가 재전송을 거르는 키 |
| `TimestampUtc`, `IsServerTime` | `IAnalyticsClock`(기본 기기 시계, 서버 시각 아님) |
| `Sequence` | 설치마다 1부터 늘어나는 순번. 100개씩 미리 예약해 PlayerPrefs 에 저장하므로 비정상 종료하면 빈 번호가 생기지만 같은 번호는 다시 나오지 않습니다. 다른 sink 로만 간 이벤트도 번호를 쓰므로 sink 마다 빈 번호가 있습니다 |
| `SessionId` | `IAnalyticsSessionSource` |
| `UserId`, `UserProperties` | `SetUserId`, `SetUserProperty` |
| `App` | 설치 ID(`IAppIdentity`), 앱 버전, 플랫폼(`android`, `ios`, `editor` 등), 환경(`AppEnv.Name`), 기기 모델, OS |

## 표준 이벤트

구매와 광고 노출 수익은 표준 이벤트로 기록합니다. 어댑터가 벤더 표준 이벤트(Firebase `purchase`, Airbridge 주문 완료, AppsFlyer `af_purchase`, Meta `LogPurchase` 등)로 바꾸고, 자사 수집·콘솔에는 그대로 갑니다.

```csharp
// 스토어 결제가 확정되고 서버가 지급한 뒤 한 번
analytics.LogPurchase(new AnalyticsPurchase("gem_100", 1.99, "USD", quantity: 1, transactionId: receipt.TransactionId),
    new Dictionary<string, object> { { "player_level", level } });

// 미디에이션 SDK 의 노출 수익 콜백마다. MAX 는 수익을 모르면 Revenue 가 -1 이므로 기록하지 않습니다
if (adInfo.Revenue >= 0)
{
    analytics.LogAdImpression(new AnalyticsAdImpression("AppLovin", adInfo.NetworkName, "rewarded", adInfo.AdUnitIdentifier, adInfo.Revenue, "USD", "shop"));
}
```

- 이벤트 이름은 `purchase`, `ad_impression` 이고 파라미터 이름은 `AnalyticsStandardParameters`(Firebase 권장 이름: `product_id`, `price`, `quantity`, `value`, `currency`, `transaction_id`, `ad_platform`, `ad_source`, `ad_format`, `ad_unit_name`, `ad_placement`)입니다. 구매의 `value` 는 단가 × 수량입니다.
- 통화는 ISO 4217 영문 3자(대문자로 저장)입니다. 빈 상품 ID·플랫폼, 음수·NaN 금액, 단가 × 수량이 무한대가 되는 값, 1 미만 수량, 잘못된 통화는 예외입니다. 추가 파라미터가 표준 이름과 겹쳐도 예외입니다.
- 어댑터는 `AnalyticsPurchase.TryRead`/`AnalyticsAdImpression.TryRead` 로 읽고, 표준이 아닌 파라미터(`IsStandardParameter` 가 false)는 함께 보냅니다. 게임별 예외는 각 어댑터 옵션의 `HandleEvent` 로 직접 보냅니다.

## sink 와 재전송

- sink 는 `AnalyticsSinks` 비트 하나를 `Id` 로 알립니다. 같은 비트는 한 번만 등록할 수 있습니다. 게임이 만든 sink 는 `Custom1`~`Custom4` 를 씁니다.
- 등록하면 지금 사용자 ID(없으면 null)와 속성을 그 sink 에 알리고, 이전 실행에서 저장된 그 sink 의 배치를 보냅니다. 저장된 배치가 지금 `MaxBatchSize` 보다 크면 나눠 저장한 뒤 보냅니다. 등록 전에 기록한 이벤트는 그 sink 로 가지 않습니다.
- sink 마다 메모리 큐가 있고 `MaxBatchSize` 개씩 `SendAsync` 로 넘깁니다. `BatchDelay` 가 있으면(자사 수집 기본 10초) 첫 이벤트 뒤 그 시간이 지나거나 배치가 차면 보냅니다.
- `SendAsync` 결과가 `Retry` 면 배치를 `StorageDirectory/<sink 이름>/` 에 JSON 파일로 저장하고 백오프 뒤 그 sink 로만 다시 보냅니다. `Rejected` 면 버립니다. sink 가 예외를 던지면 Error 로그를 남기고 그 배치를 버립니다. sink 가 다른 스레드에서 끝내도 메인 스레드로 돌아온 뒤 다음 호출을 합니다.
- 다시 보내는 때: 백오프가 끝났을 때(`RetryDelay` 5초부터 두 배씩 `MaxRetryDelay` 5분까지, 50~100% 무작위), 앱이 포그라운드로 돌아왔을 때, 네트워크가 끊겼다가 다시 연결됐을 때(1초마다 확인), 다음 실행에서 sink 를 등록할 때. 새 이벤트는 백오프를 깨지 않습니다.
- 앱이 백그라운드로 가거나 끝날 때 메모리의 이벤트(보내는 중인 배치 포함)를 디스크에 두고 배치 대기 없이 바로 보내기 시작합니다. 백그라운드에 있는 동안 기록한 이벤트는 기록하자마자 디스크에 둡니다.
- 저장 파일은 결과가 나온 뒤에 지웁니다. 보냈는데 응답 전에 앱이 죽으면 다음 실행에서 한 번 더 보낼 수 있습니다(자사 수집 서버는 ID 로 거름).
- sink 가 준비되지 않으면(`IsReady` false) 1초마다 다시 보고, 메모리 큐가 `MaxQueuedEventsPerSink`(기본 1000)를 넘으면 디스크로 옮깁니다. 저장 파일이 `MaxStoredBatchesPerSink`(기본 500)를 넘으면 가장 오래된 것부터 지웁니다(Warning).
- 디스크에 쓰지 못하면(저장 공간 부족 등) 5초 동안은 다시 쓰지 않고, 메모리 큐가 상한을 넘으면 가장 오래된 이벤트부터 버립니다(Warning, 5초에 한 번). 저장 파일을 읽거나 목록을 읽지 못하면 저장된 배치만 백오프 뒤 다시 시도하고, 그동안 새 이벤트는 계속 보냅니다.
- 저장된 배치가 많으면 한 프레임에 8개 배치까지 보내고 다음 프레임에 이어서 보냅니다.
- 저장 파일은 평문 JSON 입니다. 비밀 값을 이벤트에 넣지 않습니다.

`Dispose` 는 메모리의 이벤트를 디스크에 두고 대기와 보내는 중인 요청을 취소합니다.

## 시각

기본 시계는 기기 시계라 사용자가 시계를 바꾸면 이벤트 시각도 바뀝니다. services 의 `ITimeService`(servertime 으로 맞춘 서버 시각)를 쓰려면 다음처럼 잇습니다.

```csharp
public sealed class TimeServiceAnalyticsClock : IAnalyticsClock
{
    private readonly ITimeService _time;

    public TimeServiceAnalyticsClock(ITimeService time)
    {
        _time = time;
    }

    public DateTime UtcNow => _time.DateTimeUtcNow;

    public bool IsServerTime => _time.IsServerTime;
}
```

## 자사 수집

`CollectorSink`(http 패키지가 있을 때)는 `CollectorSinkOptions.Endpoint` 로 배치를 POST 합니다. 기본 배치 50개, 대기 10초, 요청 제한 시간 15초, 본문 상한(`MaxBodyBytes`) 1 MiB 입니다. 본문이 상한을 넘거나 서버가 413 을 주면 배치를 반으로 나눠 차례로 보내고, 이벤트 하나로도 넘으면 그 이벤트만 버립니다(Error). 나눈 조각 하나가 재시도면 배치 전체를 다시 보냅니다(이미 받은 조각은 서버가 ID 로 거름). 인증이 필요한 서버면 account 의 `AuthTokenMiddleware` 를 넣은 http 클라이언트를 넘깁니다.

| 응답 | 처리 |
|---|---|
| 2xx | 받음. 서버가 이벤트별로 거절한 것(`rejected`)은 Warning |
| 연결 실패, 타임아웃, 408, 429, 5xx, 미들웨어 실패 코드 | 재시도 |
| 413 | 반으로 나눠 다시 보냄(이벤트 하나면 버림) |
| 그 밖의 4xx | 버림(Error) |

요청 본문(`AnalyticsWire`): `{"v":1,"events":[{"id","name","time"(유닉스 ms),"serverTime","seq","sessionId","userId","installId","appVersion","platform","env","device","os","params":{...},"userProps":{...}}]}`. 값이 null 인 문자열 필드는 빠집니다. 파라미터 값은 JSON 문자열·정수·실수(소수점·지수가 있는 수)·불리언입니다. 응답은 `{"accepted":n,"duplicates":n,"rejected":[{"id","reason"}]}` 입니다.

### 수집 서버(`Server~`)

같은 형식을 받는 ASP.NET Core 라이브러리입니다(.NET 10, `Server~/Geuneda.Analytics.Server.slnx`).

```csharp
builder.Services.AddGeunedaAnalytics(o => o.MaxEventsPerBatch = 500);
builder.Services.AddSingleton<IAnalyticsEventStore, MyDbAnalyticsEventStore>();   // 이벤트 ID 고유 키로 중복 거름
app.MapGeunedaAnalytics("/analytics")          // POST /analytics/events
   .RequireRateLimiting("analytics");          // 필요하면 인증·요청 수 제한을 붙임
```

- 415: JSON 이 아님. 413: 본문(`MaxBodyBytes`, 기본 1 MiB)이나 이벤트 수(`MaxEventsPerBatch`, 기본 500) 초과. 400: 배치 형식 오류. 200: 이벤트별로 검사해 맞는 것만 저장하고 나머지는 `rejected` 에 이유와 함께 돌려줍니다.
- `IAnalyticsEventStore.AppendAsync` 는 같은 ID 를 한 번만 저장해야 합니다(재전송, 한 배치 안 중복). 예외를 던지면 500 이 되고 클라이언트가 배치를 다시 보냅니다.
- `CollectedEvent.Timestamp` 는 클라이언트 시계입니다. `IsServerTime` 이 false 면 기기 시계이니 `ReceivedAt`(서버가 받은 시각)과 함께 봅니다.
- `InMemoryAnalyticsEventStore` 는 샘플·테스트용입니다(크기 제한 없음, 다시 시작하면 사라짐).
- 샘플 서버: `dotnet run --project Server~/samples/Geuneda.Analytics.SampleServer --no-launch-profile -- --urls http://127.0.0.1:5399`

## 세션 연동

`AppSessionAnalytics`(account 패키지가 있을 때)를 `AnalyticsOptions.Session` 에 넣으면 이벤트에 `IAppSession` 의 세션 ID 가 붙습니다. `LogSessionEvents(analytics, sinks)` 를 부르면 `session_start`, `session_pause`, `session_resume`, `session_end`(파라미터 `reason`: `ended`, `restarted`, `background_timeout`, `application_quit`)를 기록합니다. 끝 이벤트에는 끝난 세션의 ID 가 붙습니다.

## sink 직접 만들기

```csharp
public sealed class MyServerSink : IAnalyticsSink
{
    public AnalyticsSinks Id => AnalyticsSinks.Custom1;
    public int MaxBatchSize => 20;
    public TimeSpan BatchDelay => TimeSpan.FromSeconds(5);
    public bool IsReady => true;

    public async UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
    {
        // 일시적 실패는 Retry(저장 후 이 sink 로만 다시 보냄), 다시 보내도 안 될 배치는 Rejected
    }

    public void SetUserId(string userId) { }
    public void SetUserProperty(string name, string value) { }
}
```

- 모든 멤버는 메인 스레드에서 불립니다. 같은 sink 의 `SendAsync` 는 한 번에 하나입니다. 다른 스레드에서 결과를 끝내도 됩니다(서비스가 메인 스레드로 돌아옴).
- `MaxBatchSize` 와 `BatchDelay` 는 등록할 때 읽습니다. 다음 실행에서 `MaxBatchSize` 를 줄이면 저장된 큰 배치는 나눠서 보냅니다. `Id` 는 바꾸지 않습니다(저장 폴더 이름).
- 서비스를 닫으면 토큰이 취소됩니다. 취소되면 `Retry` 를 돌려줍니다.

## 한계

- 앱이 포그라운드에서 비정상 종료하면 아직 보내지 않은 메모리의 이벤트는 사라집니다(자사 수집은 최대 배치 대기 시간만큼).
- 벤더 SDK 로 다시 보낸 이벤트의 시각은 SDK 가 보낸 시각으로 찍힙니다(SDK 가 과거 시각을 받지 않음).
- 순번은 sink 마다 빈 번호가 있어 유실 판단에는 쓰지 않습니다. 순서 판단에 씁니다.

## 테스트

- Unity: `Geuneda.Analytics.Tests.EditMode`, `Geuneda.Analytics.Collector.Tests.EditMode`, `Geuneda.Analytics.Account.Tests.EditMode`, `Geuneda.Analytics.Tests.PlayMode`.
- 서버: `dotnet test "Server~/Geuneda.Analytics.Server.slnx"`.
- 상호 운용: 샘플 서버를 띄우고 `GEUNEDA_ANALYTICS_INTEROP_URL=http://127.0.0.1:5399` 로 `Geuneda.Analytics.Collector.Tests.SampleServerInteropTest`(Explicit) 를 실행합니다.
