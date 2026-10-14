using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Analytics.Collector;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Collector.Tests
{
	/// <summary>
	/// 실제 .NET 샘플 수집 서버(<c>Server~/samples/Geuneda.Analytics.SampleServer</c>)와의 상호 운용을 확인합니다.
	/// 서버를 띄우고 <c>GEUNEDA_ANALYTICS_INTEROP_URL</c>(예: <c>http://127.0.0.1:5399</c>)을 설정한 뒤 이름으로 골라 실행합니다.
	/// </summary>
	[TestFixture]
	[Explicit("샘플 수집 서버가 필요합니다")]
	[Category("Interop")]
	public class SampleServerInteropTest
	{
		private const string UrlVariable = "GEUNEDA_ANALYTICS_INTEROP_URL";

		private static string ServerUrl()
		{
			var url = Environment.GetEnvironmentVariable(UrlVariable);

			if (string.IsNullOrEmpty(url))
			{
				Assert.Ignore($"{UrlVariable} is not set");
			}

			return url.TrimEnd('/');
		}

		[UnityTest]
		public IEnumerator SampleServer_StoresEventsWithTypedValuesAndDropsResends() => UniTask.ToCoroutine(async () =>
		{
			var url = ServerUrl();
			var directory = Path.Combine(Path.GetTempPath(), "geuneda-analytics-interop", Guid.NewGuid().ToString("N"));
			var http = new HttpClientBuilder().WithBaseAddress(url + "/").Build();
			var service = new AnalyticsService(new AnalyticsOptions { StorageDirectory = directory, Session = new FixedSession() }, new MemorySequenceStore(),
				new MonotonicClock(), (delay, token) => UniTask.Delay(delay, DelayType.Realtime, PlayerLoopTiming.Update, token), false);
			var ids = new IdSink();

			try
			{
				service.AddSink(new CollectorSink(http, new CollectorSinkOptions { Endpoint = "analytics/events", BatchDelay = TimeSpan.Zero }));
				service.AddSink(ids);
				service.SetUserId("interop-user");
				service.SetUserProperty("tier", "gold");

				service.LogEvent("interop_event", new Dictionary<string, object>
				{
					{ "text", "한글 2026-10-14T00:00:00Z" },
					{ "count", long.MaxValue },
					{ "ratio", 3.0 },
					{ "flag", true }
				});

				var channel = service.Channels[0];
				var deadline = DateTime.UtcNow.AddSeconds(10);

				while ((channel.IsSending || channel.QueuedCount > 0 || channel.StoredCount > 0) && DateTime.UtcNow < deadline)
				{
					await UniTask.Delay(50, DelayType.Realtime);
				}

				Assert.IsFalse(channel.IsSending || channel.QueuedCount > 0 || channel.StoredCount > 0, "the batch was delivered");

				var stored = await http.GetAsync("analytics/debug/events?id=" + ids.LastId);
				Assert.AreEqual(200, stored.Value.StatusCode, stored.ToString());
				var json = JObject.Parse(stored.Value.ReadText());
				Assert.AreEqual("interop_event", (string) json["name"]);
				Assert.AreEqual("interop-session", (string) json["sessionId"]);
				Assert.AreEqual("interop-user", (string) json["userId"]);
				Assert.AreEqual("gold", (string) json["userProperties"]["tier"]);
				Assert.AreEqual("String", (string) json["parameters"]["text"]["type"]);
				Assert.AreEqual("한글 2026-10-14T00:00:00Z", (string) json["parameters"]["text"]["value"]);
				Assert.AreEqual("Int64", (string) json["parameters"]["count"]["type"]);
				Assert.AreEqual(long.MaxValue, (long) json["parameters"]["count"]["value"]);
				Assert.AreEqual("Double", (string) json["parameters"]["ratio"]["type"]);
				Assert.AreEqual("Boolean", (string) json["parameters"]["flag"]["type"]);

				// 같은 배치를 다시 보내면(응답을 못 받은 재전송) 서버가 중복으로 셉니다
				var resend = await http.SendAsync(HttpRequest.Post("analytics/events")
					.WithBody(AnalyticsJson.SerializeBatch(new[] { ids.Last }), AnalyticsWire.ContentType));
				var counts = JObject.Parse(resend.Value.ReadText());
				Assert.AreEqual(0, (int) counts["accepted"]);
				Assert.AreEqual(1, (int) counts["duplicates"]);
			}
			finally
			{
				service.Dispose();

				if (Directory.Exists(directory))
				{
					Directory.Delete(directory, true);
				}
			}
		});

		private sealed class FixedSession : IAnalyticsSessionSource
		{
			public string SessionId => "interop-session";
		}

		/// <summary>기록된 이벤트 ID 를 알아내려고 함께 등록하는 sink 입니다.</summary>
		private sealed class IdSink : IAnalyticsSink
		{
			public AnalyticsEvent Last { get; private set; }
			public string LastId => Last?.Id;

			public AnalyticsSinks Id => AnalyticsSinks.Custom1;
			public int MaxBatchSize => 10;
			public TimeSpan BatchDelay => TimeSpan.Zero;
			public bool IsReady => true;

			public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
			{
				Last = events[events.Count - 1];
				return UniTask.FromResult(AnalyticsSendResult.Delivered);
			}

			public void SetUserId(string userId)
			{
			}

			public void SetUserProperty(string name, string value)
			{
			}
		}

		private sealed class MemorySequenceStore : ISequenceStore
		{
			private long _stored;

			public long Load()
			{
				return _stored;
			}

			public void Save(long reservedUpTo)
			{
				_stored = reservedUpTo;
			}
		}
	}
}
