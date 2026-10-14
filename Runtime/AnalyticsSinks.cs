using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트를 받을 곳(sink)입니다. sink 는 자기 비트 하나를 <see cref="IAnalyticsSink.Id"/> 로 알리고,
	/// <see cref="IAnalyticsService.LogEvent(string, System.Collections.Generic.IReadOnlyDictionary{string, object}, AnalyticsSinks)"/> 에 넘긴 비트와 겹치는 sink 에만 이벤트가 갑니다.
	/// </summary>
	/// <remarks>
	/// 값은 저장된 이벤트의 폴더 이름과 이어지므로 바꾸지 않습니다. 6~23 비트는 앞으로 추가할 geuneda 어댑터용으로 남겨 두고, 게임이 직접 만든 sink 는 <see cref="Custom1"/>~<see cref="Custom4"/> 를 씁니다.
	/// </remarks>
	[Flags]
	public enum AnalyticsSinks
	{
		None = 0,

		/// <summary>콘솔 출력(개발 확인용, <see cref="ConsoleSink"/>)</summary>
		Console = 1 << 0,

		/// <summary>자사 수집 서버(<c>Geuneda.Analytics.Collector.CollectorSink</c>)</summary>
		Collector = 1 << 1,

		/// <summary>Firebase Analytics(<c>com.geuneda.analytics.firebase</c>)</summary>
		Firebase = 1 << 2,

		/// <summary>Airbridge(<c>com.geuneda.analytics.airbridge</c>)</summary>
		Airbridge = 1 << 3,

		/// <summary>AppsFlyer(<c>com.geuneda.analytics.appsflyer</c>)</summary>
		AppsFlyer = 1 << 4,

		/// <summary>Facebook(Meta) App Events(<c>com.geuneda.analytics.facebook</c>)</summary>
		Facebook = 1 << 5,

		Custom1 = 1 << 24,
		Custom2 = 1 << 25,
		Custom3 = 1 << 26,
		Custom4 = 1 << 27,

		/// <summary>등록된 모든 sink</summary>
		All = ~None
	}
}
