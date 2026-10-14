using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트 시각을 정하는 시계입니다. 기본은 기기 시계(<see cref="DateTime.UtcNow"/>, 서버 시각 아님)이고,
	/// services 의 <c>ITimeService</c> 를 쓰는 게임은 <c>DateTimeUtcNow</c> 와 <c>IsServerTime</c> 을 넘기는 구현을 <see cref="AnalyticsOptions.Clock"/> 에 넣습니다.
	/// </summary>
	public interface IAnalyticsClock
	{
		/// <summary>지금 시각(UTC)</summary>
		DateTime UtcNow { get; }

		/// <summary><see cref="UtcNow"/> 가 서버 시각으로 보정된 값인지</summary>
		bool IsServerTime { get; }
	}
}
