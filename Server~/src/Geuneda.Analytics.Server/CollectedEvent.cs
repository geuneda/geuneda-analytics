using System;
using System.Collections.Generic;

namespace Geuneda.Analytics.Server
{
	/// <summary>
	/// 받은 이벤트 하나입니다. 클라이언트가 보낸 값(<see cref="AnalyticsWire"/> 형식)과 서버가 받은 시각을 담습니다.
	/// <see cref="Timestamp"/> 는 클라이언트 시계이므로(<see cref="IsServerTime"/> 이 false 면 기기 시계) 분석할 때 <see cref="ReceivedAt"/> 과 함께 봅니다.
	/// </summary>
	public sealed class CollectedEvent
	{
		public CollectedEvent(string id, string name, DateTimeOffset timestamp, bool isServerTime, long sequence, string sessionId, string userId,
			string installId, string appVersion, string platform, string environment, string device, string operatingSystem,
			IReadOnlyDictionary<string, object> parameters, IReadOnlyDictionary<string, string> userProperties, DateTimeOffset receivedAt)
		{
			Id = id;
			Name = name;
			Timestamp = timestamp;
			IsServerTime = isServerTime;
			Sequence = sequence;
			SessionId = sessionId;
			UserId = userId;
			InstallId = installId;
			AppVersion = appVersion;
			Platform = platform;
			Environment = environment;
			Device = device;
			OperatingSystem = operatingSystem;
			Parameters = parameters;
			UserProperties = userProperties;
			ReceivedAt = receivedAt;
		}

		/// <summary>클라이언트가 만든 이벤트 ID. 재전송된 이벤트를 거르는 키입니다.</summary>
		public string Id { get; }

		public string Name { get; }

		/// <summary>클라이언트가 기록한 시각(UTC, ms 정밀도)</summary>
		public DateTimeOffset Timestamp { get; }

		/// <summary><see cref="Timestamp"/> 가 서버 시각으로 보정된 값인지</summary>
		public bool IsServerTime { get; }

		/// <summary>설치마다 늘어나는 순번. 비는 번호가 있을 수 있습니다(다른 sink 로만 간 이벤트, 비정상 종료로 건너뛴 예약).</summary>
		public long Sequence { get; }

		public string SessionId { get; }

		public string UserId { get; }

		public string InstallId { get; }

		public string AppVersion { get; }

		public string Platform { get; }

		public string Environment { get; }

		public string Device { get; }

		public string OperatingSystem { get; }

		/// <summary>파라미터. 값은 <c>string</c>, <c>long</c>(JSON 정수), <c>double</c>(소수점·지수가 있는 수), <c>bool</c> 입니다.</summary>
		public IReadOnlyDictionary<string, object> Parameters { get; }

		public IReadOnlyDictionary<string, string> UserProperties { get; }

		/// <summary>서버가 받은 시각</summary>
		public DateTimeOffset ReceivedAt { get; }
	}
}
