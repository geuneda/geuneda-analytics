using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 기록한 이벤트 하나입니다. 이벤트 이름과 파라미터에 기록한 순간의 공통 맥락(시각, 순번, 세션, 사용자, 앱)을 함께 담고, 바뀌지 않습니다.
	/// 저장했다가 다시 보낼 때도 이 값을 그대로 보냅니다.
	/// </summary>
	public sealed class AnalyticsEvent
	{
		private static readonly IReadOnlyDictionary<string, string> NoUserProperties = new Dictionary<string, string>();

		/// <param name="id">이벤트 ID. 재전송된 이벤트를 수집 서버가 걸러내는 키입니다</param>
		/// <param name="name">이벤트 이름</param>
		/// <param name="parameters">파라미터. 이 목록을 그대로 쥐므로 넘긴 뒤에 바꾸지 않습니다. null 은 빈 목록</param>
		/// <param name="timestampUtc">기록한 시각(UTC)</param>
		/// <param name="isServerTime">시각이 서버 시각으로 보정된 값인지</param>
		/// <param name="sequence">설치마다 늘어나는 순번(1부터, 비는 번호가 있을 수 있음)</param>
		/// <param name="sessionId">세션 ID. 없으면 null</param>
		/// <param name="userId">사용자 ID. 없으면 null</param>
		/// <param name="userProperties">기록한 순간의 사용자 속성. 이 사전을 그대로 쥐므로 넘긴 뒤에 바꾸지 않습니다. null 은 빈 사전</param>
		/// <param name="app">앱 정보</param>
		public AnalyticsEvent(string id, string name, IReadOnlyList<AnalyticsParameter> parameters, DateTime timestampUtc, bool isServerTime,
			long sequence, string sessionId, string userId, IReadOnlyDictionary<string, string> userProperties, AnalyticsAppInfo app)
		{
			if (string.IsNullOrEmpty(id))
			{
				throw new ArgumentException("Event id is empty", nameof(id));
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("Event name is empty", nameof(name));
			}

			if (timestampUtc.Kind == DateTimeKind.Local)
			{
				throw new ArgumentException("Event timestamp must be UTC", nameof(timestampUtc));
			}

			Id = id;
			Name = name;
			Parameters = parameters ?? Array.Empty<AnalyticsParameter>();
			TimestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
			IsServerTime = isServerTime;
			Sequence = sequence;
			SessionId = sessionId;
			UserId = userId;
			UserProperties = userProperties ?? NoUserProperties;
			App = app ?? throw new ArgumentNullException(nameof(app));
		}

		public string Id { get; }

		public string Name { get; }

		public IReadOnlyList<AnalyticsParameter> Parameters { get; }

		public DateTime TimestampUtc { get; }

		public bool IsServerTime { get; }

		public long Sequence { get; }

		public string SessionId { get; }

		public string UserId { get; }

		public IReadOnlyDictionary<string, string> UserProperties { get; }

		public AnalyticsAppInfo App { get; }

		/// <summary>이름이 <paramref name="name"/> 인 파라미터를 찾습니다.</summary>
		public bool TryGetParameter(string name, out AnalyticsValue value)
		{
			for (var i = 0; i < Parameters.Count; i++)
			{
				if (string.Equals(Parameters[i].Name, name, StringComparison.Ordinal))
				{
					value = Parameters[i].Value;
					return true;
				}
			}

			value = default;
			return false;
		}

		public override string ToString()
		{
			return $"{Name} #{Sequence} ({Id})";
		}
	}
}
