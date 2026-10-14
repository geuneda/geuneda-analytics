// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트 배치 JSON 의 형식입니다. 저장 파일과 수집 서버 요청이 같은 형식을 쓰고, 서버(<c>Server~</c>)가 이 파일을 링크합니다.
	/// </summary>
	/// <remarks>
	/// 배치: <c>{"v":1,"events":[이벤트...]}</c>. 이벤트: <c>id</c>, <c>name</c>, <c>time</c>(유닉스 ms), <c>serverTime</c>, <c>seq</c>, <c>params</c>(객체, 값은 문자열·정수·실수·불리언),
	/// 선택 필드 <c>sessionId</c>, <c>userId</c>, <c>installId</c>, <c>appVersion</c>, <c>platform</c>, <c>env</c>, <c>device</c>, <c>os</c>(문자열, 없으면 빠짐), <c>userProps</c>(문자열 값 객체).
	/// 모르는 필드는 무시합니다. 응답(200): <c>{"accepted":n,"duplicates":n,"rejected":[{"id":"..","reason":".."}]}</c>.
	/// </remarks>
	public static class AnalyticsWire
	{
		/// <summary>배치 형식 버전입니다. 형식을 호환되지 않게 바꿀 때만 올립니다.</summary>
		public const int Version = 1;

		public const string ContentType = "application/json";

		public const string BatchVersion = "v";
		public const string BatchEvents = "events";

		public const string Id = "id";
		public const string Name = "name";
		public const string Time = "time";
		public const string ServerTime = "serverTime";
		public const string Sequence = "seq";
		public const string SessionId = "sessionId";
		public const string UserId = "userId";
		public const string InstallId = "installId";
		public const string AppVersion = "appVersion";
		public const string Platform = "platform";
		public const string Environment = "env";
		public const string Device = "device";
		public const string OperatingSystem = "os";
		public const string Parameters = "params";
		public const string UserProperties = "userProps";

		public const string ResponseAccepted = "accepted";
		public const string ResponseDuplicates = "duplicates";
		public const string ResponseRejected = "rejected";
		public const string ResponseRejectedId = "id";
		public const string ResponseRejectedReason = "reason";
	}
}
