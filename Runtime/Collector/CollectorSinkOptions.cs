using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Collector
{
	/// <summary><see cref="CollectorSink"/> 설정입니다.</summary>
	public sealed class CollectorSinkOptions
	{
		/// <summary>배치 크기의 상한입니다. 서버(<c>AnalyticsServerOptions.MaxEventsPerBatch</c>, 기본 500)보다 크게 두지 않습니다.</summary>
		public const int MaxAllowedBatchSize = 500;

		/// <summary>
		/// 배치를 보낼 주소입니다. http 클라이언트의 기본 주소 기준 상대 주소(예: <c>analytics/events</c>)나 절대 주소입니다. 서버의 <c>MapGeunedaAnalytics("/analytics")</c> 는 <c>/analytics/events</c> 를 받습니다.
		/// </summary>
		public string Endpoint { get; set; }

		/// <summary>한 요청에 담는 최대 이벤트 수입니다. 기본 50, 1~500 입니다.</summary>
		public int MaxBatchSize { get; set; } = 50;

		/// <summary>첫 이벤트가 쌓인 뒤 배치를 보내기까지 기다리는 시간입니다. 기본 10초입니다. 0 이면 이벤트마다 보냅니다.</summary>
		public TimeSpan BatchDelay { get; set; } = TimeSpan.FromSeconds(10);

		/// <summary>
		/// 요청 본문 크기 상한(바이트)입니다. 넘으면 배치를 나눠 보내고, 이벤트 하나로도 넘으면 그 이벤트를 버립니다. 기본 1 MiB(서버 <c>AnalyticsServerOptions.MaxBodyBytes</c> 기본값과 같음)입니다.
		/// 서버 상한보다 크게 두면 서버가 413 을 주고, 그때도 나눠 보냅니다.
		/// </summary>
		public int MaxBodyBytes { get; set; } = 1024 * 1024;

		/// <summary>요청 제한 시간입니다. 기본 15초입니다. null 이면 http 클라이언트 설정을 따릅니다.</summary>
		public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(15);
	}
}
