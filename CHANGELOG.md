# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-14

**New**:
- 분석 서비스 `IAnalyticsService`/`AnalyticsService`: `LogEvent(name, parameters[, sinks])`, `SetUserId`, `SetUserProperty`, `AddSink`. 이벤트마다 기록 시점의 ID(GUID), 시각과 서버 시각 여부(`IAnalyticsClock`), 설치별 순번(100개씩 예약), 세션 ID(`IAnalyticsSessionSource`), 사용자 ID·속성, 앱 정보(설치 ID, 버전, 플랫폼, 환경, 기기, OS)를 담음. 메인 스레드 전용, 오용은 예외
- 값 `AnalyticsValue`: 문자열·64비트 정수·유한 실수·불리언만 받아 저장·재전송해도 형식 유지. enum 등 그 밖의 형식은 `ArgumentException`
- sink 비트 `AnalyticsSinks`(Console, Collector, Firebase, Airbridge, AppsFlyer, Facebook, Custom1~4)와 계약 `IAnalyticsSink`(`MaxBatchSize`, `BatchDelay`, `IsReady`, `SendAsync` → `Delivered`/`Retry`/`Rejected`)
- sink 별 큐와 재전송: 실패한 sink 의 배치만 `StorageDirectory/<sink>/` 에 평문 JSON 으로 저장해 그 sink 로만 다시 보냄. 백오프(5초부터 두 배씩 5분, 50~100% 무작위), 포그라운드 복귀, 네트워크 복구, 다음 실행에서 재전송. 파일은 결과가 나온 뒤 삭제. 일시정지·종료 때 메모리 이벤트와 보내는 중인 배치를 저장. 큐·저장 상한(디스크 실패 때도 메모리 상한 유지, 오래된 것부터 버림), 손상 파일 삭제, 읽기·목록 실패는 저장된 배치만 백오프 후 재시도(새 이벤트는 계속 전송), 지금보다 큰 저장 배치는 나눠 보냄, 한 프레임 8배치, sink 가 다른 스레드에서 끝내도 메인 스레드로 복귀, 취소 콜백 예외 격리
- `IAnalyticsService` 는 `IDisposable`(`MainInstaller.CleanDispose<IAnalyticsService>()`)
- `ConsoleSink`: core 로거로 `이름 {키=값}` 출력
- 자사 수집 `Geuneda.Analytics.Collector.CollectorSink`(http 패키지가 있을 때): 배치 POST(기본 50개, 10초 대기, 15초 제한 시간, 본문 1 MiB), 2xx 받음, 연결 실패·408·429·5xx 재시도, 본문 초과·413 은 반으로 나눠 다시 보냄(이벤트 하나면 버림), 그 밖 4xx 버림
- 세션 연동 `Geuneda.Analytics.Account.AppSessionAnalytics`(account 패키지가 있을 때): 세션 ID 와 `session_start`/`session_pause`/`session_resume`/`session_end`(이유) 이벤트
- 배치 형식 `AnalyticsWire`(버전 1)
- ASP.NET Core 수집 서버(`Server~`, .NET 10): `AddGeunedaAnalytics`, `MapGeunedaAnalytics`(`POST {prefix}/events`), 이벤트별 검증과 거절 이유(정수 표기가 64비트를 넘으면 거절, 클라이언트와 같은 규칙), 크기 제한(413), 저장소 `IAnalyticsEventStore`(ID 로 중복 제거), `InMemoryAnalyticsEventStore`, 샘플 서버
