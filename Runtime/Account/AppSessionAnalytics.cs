using System;
using System.Collections.Generic;
using Geuneda.Account;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Account
{
	/// <summary>
	/// account 의 <see cref="IAppSession"/> 을 분석 이벤트에 잇습니다. <see cref="AnalyticsOptions.Session"/> 에 넣으면 이벤트에 세션 ID 가 붙고,
	/// <see cref="LogSessionEvents"/> 를 부르면 세션 시작·일시정지·재개·끝 이벤트를 기록합니다. 메인 스레드에서 씁니다.
	/// </summary>
	/// <remarks>
	/// 세션 끝 이벤트에는 끝난 세션의 ID 가 붙습니다. 다른 <see cref="IAppSession.Ended"/> 처리기가 기록한 이벤트에는 세션 ID 가 없습니다(세션이 이미 끝남).
	/// <c>session_start</c> 같은 이름은 Firebase 가 자동 이벤트로 예약해 두었으니 이 이벤트는 자사 수집(<see cref="AnalyticsSinks.Collector"/>)으로 보냅니다.
	/// </remarks>
	public sealed class AppSessionAnalytics : IAnalyticsSessionSource, IDisposable
	{
		public const string SessionStartEvent = "session_start";
		public const string SessionPauseEvent = "session_pause";
		public const string SessionResumeEvent = "session_resume";
		public const string SessionEndEvent = "session_end";

		/// <summary>세션 끝 이벤트의 이유 파라미터 이름입니다. 값은 <c>ended</c>, <c>restarted</c>, <c>background_timeout</c>, <c>application_quit</c> 입니다.</summary>
		public const string ReasonParameter = "reason";

		private readonly IAppSession _session;
		private IAnalyticsService _analytics;
		private AnalyticsSinks _sinks;
		private string _endingSessionId;
		private bool _subscribed;

		public AppSessionAnalytics(IAppSession session)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
		}

		/// <inheritdoc />
		public string SessionId => _endingSessionId ?? _session.SessionId;

		/// <summary>세션 이벤트를 <paramref name="sinks"/> 로 기록하기 시작합니다. 한 번만 부릅니다(두 번째는 <see cref="InvalidOperationException"/>).</summary>
		/// <param name="analytics">기록할 서비스</param>
		/// <param name="sinks">보낼 곳. 보통 <see cref="AnalyticsSinks.Collector"/></param>
		public void LogSessionEvents(IAnalyticsService analytics, AnalyticsSinks sinks)
		{
			if (analytics == null)
			{
				throw new ArgumentNullException(nameof(analytics));
			}

			if (sinks == AnalyticsSinks.None)
			{
				throw new ArgumentException("No sinks selected", nameof(sinks));
			}

			if (_subscribed)
			{
				throw new InvalidOperationException("Session events are already being logged");
			}

			_analytics = analytics;
			_sinks = sinks;
			_subscribed = true;
			_session.Started += OnStarted;
			_session.Paused += OnPaused;
			_session.Resumed += OnResumed;
			_session.Ended += OnEnded;
		}

		/// <summary>세션 이벤트 구독을 끊습니다. 세션 ID 는 계속 줍니다.</summary>
		public void Dispose()
		{
			if (!_subscribed)
			{
				return;
			}

			_subscribed = false;
			_session.Started -= OnStarted;
			_session.Paused -= OnPaused;
			_session.Resumed -= OnResumed;
			_session.Ended -= OnEnded;
		}

		private void OnStarted(SessionInfo session)
		{
			_analytics.LogEvent(SessionStartEvent, null, _sinks);
		}

		private void OnPaused(SessionInfo session)
		{
			_analytics.LogEvent(SessionPauseEvent, null, _sinks);
		}

		private void OnResumed(SessionInfo session)
		{
			_analytics.LogEvent(SessionResumeEvent, null, _sinks);
		}

		private void OnEnded(SessionInfo session, SessionEndReason reason)
		{
			_endingSessionId = session.SessionId;

			try
			{
				_analytics.LogEvent(SessionEndEvent, new Dictionary<string, object> { { ReasonParameter, ReasonName(reason) } }, _sinks);
			}
			finally
			{
				_endingSessionId = null;
			}
		}

		private static string ReasonName(SessionEndReason reason)
		{
			switch (reason)
			{
				case SessionEndReason.Ended:
					return "ended";
				case SessionEndReason.Restarted:
					return "restarted";
				case SessionEndReason.BackgroundTimeout:
					return "background_timeout";
				case SessionEndReason.ApplicationQuit:
					return "application_quit";
				default:
					return ((int) reason).ToString(System.Globalization.CultureInfo.InvariantCulture);
			}
		}
	}
}
