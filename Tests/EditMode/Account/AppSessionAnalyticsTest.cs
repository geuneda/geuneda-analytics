using System;
using System.Collections.Generic;
using System.Linq;
using Geuneda.Account;
using Geuneda.Analytics.Account;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Account.Tests
{
	public sealed class AppSessionAnalyticsTest
	{
		[Test]
		public void SessionId_FollowsTheAppSession()
		{
			var session = new AppSession();
			var source = new AppSessionAnalytics(session);

			Assert.IsNull(source.SessionId);

			var started = session.Start();
			Assert.AreEqual(started.SessionId, source.SessionId);

			session.End();
			Assert.IsNull(source.SessionId);
		}

		[Test]
		public void LogSessionEvents_LogsStartAndEndWithTheirOwnSessionIds()
		{
			var session = new AppSession();
			var source = new AppSessionAnalytics(session);
			var analytics = new RecordingAnalytics(source);
			source.LogSessionEvents(analytics, AnalyticsSinks.Collector);

			var first = session.Start();
			var second = session.Start();
			session.End();

			CollectionAssert.AreEqual(
				new[]
				{
					(AppSessionAnalytics.SessionStartEvent, first.SessionId, (string) null),
					(AppSessionAnalytics.SessionEndEvent, first.SessionId, "restarted"),
					(AppSessionAnalytics.SessionStartEvent, second.SessionId, null),
					(AppSessionAnalytics.SessionEndEvent, second.SessionId, "ended")
				},
				analytics.Logged.Select(e => (e.Name, e.SessionId, e.Reason)));
			Assert.IsTrue(analytics.Logged.All(e => e.Sinks == AnalyticsSinks.Collector));
			Assert.IsNull(source.SessionId);
		}

		[Test]
		public void LogSessionEvents_LogsPauseAndResume()
		{
			var session = new FakeAppSession();
			var source = new AppSessionAnalytics(session);
			var analytics = new RecordingAnalytics(source);
			source.LogSessionEvents(analytics, AnalyticsSinks.Collector | AnalyticsSinks.Console);

			session.RaisePaused();
			session.RaiseResumed();
			session.RaiseEnded(SessionEndReason.BackgroundTimeout);
			session.RaiseEnded(SessionEndReason.ApplicationQuit);

			CollectionAssert.AreEqual(
				new[] { AppSessionAnalytics.SessionPauseEvent, AppSessionAnalytics.SessionResumeEvent, AppSessionAnalytics.SessionEndEvent, AppSessionAnalytics.SessionEndEvent },
				analytics.Logged.Select(e => e.Name));
			CollectionAssert.AreEqual(new[] { null, null, "background_timeout", "application_quit" }, analytics.Logged.Select(e => e.Reason));
		}

		[Test]
		public void LogSessionEvents_RejectsMisuseAndDisposeStopsLogging()
		{
			var session = new FakeAppSession();
			var source = new AppSessionAnalytics(session);
			var analytics = new RecordingAnalytics(source);

			Assert.Throws<ArgumentNullException>(() => new AppSessionAnalytics(null));
			Assert.Throws<ArgumentNullException>(() => source.LogSessionEvents(null, AnalyticsSinks.Collector));
			Assert.Throws<ArgumentException>(() => source.LogSessionEvents(analytics, AnalyticsSinks.None));

			source.LogSessionEvents(analytics, AnalyticsSinks.Collector);
			Assert.Throws<InvalidOperationException>(() => source.LogSessionEvents(analytics, AnalyticsSinks.Collector));

			source.Dispose();
			session.RaisePaused();

			Assert.AreEqual(0, analytics.Logged.Count);
			Assert.AreEqual(0, session.HandlerCount);
		}

		private sealed class RecordingAnalytics : IAnalyticsService
		{
			private readonly IAnalyticsSessionSource _session;

			public RecordingAnalytics(IAnalyticsSessionSource session)
			{
				_session = session;
			}

			public List<(string Name, string SessionId, string Reason, AnalyticsSinks Sinks)> Logged { get; } =
				new List<(string, string, string, AnalyticsSinks)>();

			public AnalyticsSinks DefaultSinks => AnalyticsSinks.All;
			public string UserId => null;
			public IReadOnlyDictionary<string, string> UserProperties => new Dictionary<string, string>();

			public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null)
			{
				LogEvent(name, parameters, DefaultSinks);
			}

			public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters, AnalyticsSinks sinks)
			{
				object reason = null;
				parameters?.TryGetValue(AppSessionAnalytics.ReasonParameter, out reason);
				Logged.Add((name, _session.SessionId, (string) reason, sinks));
			}

			public void SetUserId(string userId)
			{
			}

			public void SetUserProperty(string name, string value)
			{
			}

			public void AddSink(IAnalyticsSink sink)
			{
			}

			public void Dispose()
			{
			}
		}

		private sealed class FakeAppSession : IAppSession
		{
			public bool IsActive => true;
			public string SessionId => "fake";
			public string NextSessionId => "next";
			public SessionInfo Current => new SessionInfo("fake", DateTime.UtcNow);

			public event Action<SessionInfo> Started;
			public event Action<SessionInfo> Paused;
			public event Action<SessionInfo> Resumed;
			public event Action<SessionInfo, SessionEndReason> Ended;

			public void Dispose()
			{
			}

			public int HandlerCount => (Started?.GetInvocationList().Length ?? 0) + (Paused?.GetInvocationList().Length ?? 0) +
			                           (Resumed?.GetInvocationList().Length ?? 0) + (Ended?.GetInvocationList().Length ?? 0);

			public SessionInfo Start()
			{
				Started?.Invoke(Current);
				return Current;
			}

			public void End()
			{
				Ended?.Invoke(Current, SessionEndReason.Ended);
			}

			public void RaisePaused()
			{
				Paused?.Invoke(Current);
			}

			public void RaiseResumed()
			{
				Resumed?.Invoke(Current);
			}

			public void RaiseEnded(SessionEndReason reason)
			{
				Ended?.Invoke(Current, reason);
			}
		}
	}
}
