namespace Geuneda.Analytics.Server
{
	/// <summary>수집 엔드포인트 설정입니다. 넘는 요청이나 이벤트는 거절합니다.</summary>
	public sealed class AnalyticsServerOptions
	{
		/// <summary>요청 본문 크기 상한(바이트)입니다. 넘으면 413 입니다. 기본 1 MiB 입니다.</summary>
		public int MaxBodyBytes { get; set; } = 1024 * 1024;

		/// <summary>한 요청의 이벤트 수 상한입니다. 넘으면 413 입니다. 기본 500 입니다(클라이언트 <c>CollectorSinkOptions.MaxBatchSize</c> 상한과 같음).</summary>
		public int MaxEventsPerBatch { get; set; } = 500;

		/// <summary>이벤트 이름, 파라미터·사용자 속성 이름의 길이 상한입니다. 기본 100 입니다.</summary>
		public int MaxNameLength { get; set; } = 100;

		/// <summary>이벤트 하나의 파라미터 수와 사용자 속성 수 상한입니다. 기본 100 입니다.</summary>
		public int MaxParameters { get; set; } = 100;

		/// <summary>문자열 값(파라미터, 사용자 속성, 앱 정보, ID) 길이 상한입니다. 기본 1000 입니다.</summary>
		public int MaxStringLength { get; set; } = 1000;
	}
}
