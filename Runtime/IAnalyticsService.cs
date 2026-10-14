using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 분석 이벤트를 기록해 등록된 sink 들에 나눠 보냅니다. 기본 구현은 <see cref="AnalyticsService"/> 이고,
	/// services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다(정리는 <c>MainInstaller.CleanDispose&lt;IAnalyticsService&gt;()</c>).
	/// 메인 스레드에서 씁니다(다른 스레드면 <see cref="InvalidOperationException"/>).
	/// </summary>
	/// <remarks>
	/// <see cref="IDisposable.Dispose"/> 는 앱 수명 연결을 끊고, 메모리의 이벤트(보내는 중인 배치 포함)를 디스크에 둔 뒤 대기와 보내는 중인 요청을 취소합니다.
	/// 다음 실행에서 sink 를 등록하면 다시 보냅니다. 이후 호출은 <see cref="ObjectDisposedException"/> 이고, 여러 번 불러도 됩니다.
	/// 세션 연동(<c>AppSessionAnalytics</c>)의 구독은 이보다 먼저 해제합니다.
	/// </remarks>
	public interface IAnalyticsService : IDisposable
	{
		/// <summary>sink 를 고르지 않은 <see cref="LogEvent(string, IReadOnlyDictionary{string, object})"/> 가 보내는 곳입니다(<see cref="AnalyticsOptions.DefaultSinks"/>).</summary>
		AnalyticsSinks DefaultSinks { get; }

		/// <summary>지금 사용자 ID. 없으면 null 입니다.</summary>
		string UserId { get; }

		/// <summary>지금 사용자 속성입니다. 바뀔 때마다 새 사전으로 바뀌므로 받아 둔 사전은 그대로입니다.</summary>
		IReadOnlyDictionary<string, string> UserProperties { get; }

		/// <summary><see cref="DefaultSinks"/> 로 이벤트를 기록합니다.</summary>
		/// <param name="name">이벤트 이름. 비었거나 공백뿐이면 <see cref="System.ArgumentException"/></param>
		/// <param name="parameters">파라미터. 값 형식은 <see cref="AnalyticsValue.FromObject"/> 를 따르고, 지원하지 않는 형식이면 <see cref="System.ArgumentException"/></param>
		void LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null);

		/// <summary>
		/// <paramref name="sinks"/> 에 해당하는 등록된 sink 로 이벤트를 기록합니다. 등록되지 않은 sink 비트는 무시합니다.
		/// <see cref="AnalyticsSinks.None"/> 은 <see cref="System.ArgumentException"/> 입니다.
		/// </summary>
		void LogEvent(string name, IReadOnlyDictionary<string, object> parameters, AnalyticsSinks sinks);

		/// <summary>사용자 ID 를 정하고 모든 sink 에 알립니다. null 이나 빈 문자열이면 해제입니다. 이후 이벤트부터 붙습니다.</summary>
		void SetUserId(string userId);

		/// <summary>사용자 속성을 정하고 모든 sink 에 알립니다. <paramref name="value"/> 가 null 이면 제거입니다. 이후 이벤트부터 붙습니다.</summary>
		void SetUserProperty(string name, string value);

		/// <summary>
		/// sink 를 등록합니다. 지금 사용자 ID 와 속성을 sink 에 알리고, 이전 실행에서 저장된 그 sink 의 이벤트를 다시 보냅니다.
		/// 등록 전에 기록한 이벤트는 이 sink 로 가지 않으므로 부트스트랩에서 먼저 등록합니다(SDK 초기화를 기다리지 말고 <see cref="IAnalyticsSink.IsReady"/> 로 알림).
		/// </summary>
		/// <exception cref="System.ArgumentException">비트가 하나가 아니거나 배치 설정이 잘못됨</exception>
		/// <exception cref="System.InvalidOperationException">같은 비트의 sink 가 이미 있음</exception>
		void AddSink(IAnalyticsSink sink);
	}
}
