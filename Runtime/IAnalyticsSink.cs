using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트를 받는 곳(SDK 어댑터, 수집 서버, 콘솔)입니다. <see cref="IAnalyticsService.AddSink"/> 로 등록하면 서비스가 sink 마다 큐를 두고
	/// 배치로 <see cref="SendAsync"/> 를 부릅니다. 모든 멤버는 메인 스레드에서 불립니다.
	/// </summary>
	public interface IAnalyticsSink
	{
		/// <summary>이 sink 의 비트 하나. 등록 뒤에 바꾸지 않습니다.</summary>
		AnalyticsSinks Id { get; }

		/// <summary>한 번에 넘길 최대 이벤트 수(1 이상). 실패한 배치는 이 크기로 저장되므로 등록 뒤에 줄이지 않습니다.</summary>
		int MaxBatchSize { get; }

		/// <summary>
		/// 첫 이벤트가 쌓인 뒤 배치를 보내기까지 기다리는 시간입니다. <see cref="TimeSpan.Zero"/> 면 기록하자마자 보냅니다(SDK 어댑터).
		/// 저장했던 이벤트와 앱이 백그라운드로 갈 때 쌓인 이벤트는 기다리지 않고 보냅니다.
		/// </summary>
		TimeSpan BatchDelay { get; }

		/// <summary>지금 보낼 수 있는지(예: SDK 초기화 끝남). false 면 이벤트가 큐에서 기다리고 서비스가 1초마다 다시 봅니다.</summary>
		bool IsReady { get; }

		/// <summary>
		/// 이벤트를 보냅니다. 같은 sink 에는 한 번에 하나만 부릅니다.
		/// <see cref="AnalyticsSendResult.Retry"/> 면 배치를 디스크에 저장했다가 백오프 뒤에 이 sink 로만 다시 보내고,
		/// <see cref="AnalyticsSendResult.Rejected"/> 면 버립니다. 예외는 버그로 보고 Error 로그를 남긴 뒤 배치를 버립니다.
		/// </summary>
		/// <param name="events">보낼 이벤트(1개 이상, <see cref="MaxBatchSize"/> 이하). 호출이 끝난 뒤에 쥐고 있지 않습니다</param>
		/// <param name="cancellationToken">서비스를 닫을 때 취소됩니다. 취소되면 <see cref="AnalyticsSendResult.Retry"/> 를 돌려줍니다</param>
		UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken);

		/// <summary>사용자 ID 를 알립니다. null 이면 해제입니다. 등록할 때 현재 값으로 한 번 불립니다.</summary>
		void SetUserId(string userId);

		/// <summary>사용자 속성을 알립니다. <paramref name="value"/> 가 null 이면 제거입니다. 등록할 때 현재 속성마다 한 번씩 불립니다.</summary>
		void SetUserProperty(string name, string value);
	}
}
