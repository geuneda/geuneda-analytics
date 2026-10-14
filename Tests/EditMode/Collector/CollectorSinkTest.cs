using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Analytics.Collector;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Collector.Tests
{
	public sealed class CollectorSinkTest
	{
		private static readonly AnalyticsAppInfo App = new AnalyticsAppInfo("install", "1.0.0", "editor", "dev", "Mac", "macOS");

		private FakeTransport _transport;
		private IHttpClient _http;

		[SetUp]
		public void SetUp()
		{
			_transport = new FakeTransport();
			_http = new HttpClientBuilder().WithBaseAddress("https://collector.test/v1/").WithTransport(_transport).Build();
		}

		private static AnalyticsEvent Event(string name)
		{
			return new AnalyticsEvent(Guid.NewGuid().ToString("N"), name, new[] { new AnalyticsParameter("n", AnalyticsValue.Integer(1)) },
				DateTime.UtcNow, false, 1, null, null, null, App);
		}

		private CollectorSink Sink(Action<CollectorSinkOptions> configure = null)
		{
			var options = new CollectorSinkOptions { Endpoint = "analytics/events" };
			configure?.Invoke(options);
			return new CollectorSink(_http, options);
		}

		private static AnalyticsSendResult Run(UniTask<AnalyticsSendResult> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the send to complete synchronously");
			return task.GetAwaiter().GetResult();
		}

		[Test]
		public void Send_PostsBatchJsonToEndpoint()
		{
			var sink = Sink();
			var events = new[] { Event("a"), Event("b") };

			Assert.AreEqual(AnalyticsSendResult.Delivered, Run(sink.SendAsync(events, CancellationToken.None)));

			var request = _transport.Requests[0];
			Assert.AreEqual(HttpMethod.Post, request.Method);
			Assert.AreEqual("https://collector.test/v1/analytics/events", request.Uri.ToString());
			Assert.AreEqual(AnalyticsWire.ContentType, request.ContentType);
			Assert.AreEqual(TimeSpan.FromSeconds(15), request.Timeout);
			Assert.IsTrue(AnalyticsJson.TryDeserializeBatch(request.Body, out var sent, out var error), error);
			Assert.AreEqual(events[0].Id, sent[0].Id);
			Assert.AreEqual("b", sent[1].Name);
			Assert.AreEqual(AnalyticsSinks.Collector, sink.Id);
			Assert.AreEqual(50, sink.MaxBatchSize);
			Assert.AreEqual(TimeSpan.FromSeconds(10), sink.BatchDelay);
		}

		[TestCase(200, AnalyticsSendResult.Delivered)]
		[TestCase(204, AnalyticsSendResult.Delivered)]
		[TestCase(408, AnalyticsSendResult.Retry)]
		[TestCase(429, AnalyticsSendResult.Retry)]
		[TestCase(500, AnalyticsSendResult.Retry)]
		[TestCase(503, AnalyticsSendResult.Retry)]
		[TestCase(400, AnalyticsSendResult.Rejected)]
		[TestCase(401, AnalyticsSendResult.Rejected)]
		[TestCase(413, AnalyticsSendResult.Rejected)]
		public void Send_MapsStatusCodes(int status, AnalyticsSendResult expected)
		{
			_transport.Respond = request => FakeTransport.Ok(request, status, status == 400 ? "{\"error\":\"bad\"}" : "");

			if (expected == AnalyticsSendResult.Retry)
			{
				LogAssert.Expect(LogType.Warning, new Regex($"HTTP {status}"));
			}
			else if (status == 413)
			{
				LogAssert.Expect(LogType.Error, new Regex("Dropping analytics event a .*HTTP 413"));
			}
			else if (expected == AnalyticsSendResult.Rejected)
			{
				LogAssert.Expect(LogType.Error, new Regex($"rejected 1 events with HTTP {status}"));
			}

			Assert.AreEqual(expected, Run(Sink().SendAsync(new[] { Event("a") }, CancellationToken.None)));
		}

		[Test]
		public void Send_RetriesWhenThereIsNoResponse()
		{
			_transport.Respond = _ => Result<HttpResponse>.Fail(ResultCodes.NetworkError, "offline");
			LogAssert.Expect(LogType.Warning, new Regex("Could not send 1 analytics events"));
			Assert.AreEqual(AnalyticsSendResult.Retry, Run(Sink().SendAsync(new[] { Event("a") }, CancellationToken.None)));

			// 취소는 로그 없이 재시도입니다
			_transport.Respond = _ => Result<HttpResponse>.Fail(ResultCodes.Cancelled);
			Assert.AreEqual(AnalyticsSendResult.Retry, Run(Sink().SendAsync(new[] { Event("a") }, CancellationToken.None)));
			LogAssert.NoUnexpectedReceived();
		}

		[Test]
		public void Send_LogsEventsRejectedByTheServer()
		{
			_transport.Respond = request => FakeTransport.Ok(request, 200,
				"{\"accepted\":1,\"duplicates\":0,\"rejected\":[{\"id\":\"x1\",\"reason\":\"name too long\"},5]}");

			LogAssert.Expect(LogType.Warning, new Regex("rejected 2 events: x1 \\(name too long\\)"));
			Assert.AreEqual(AnalyticsSendResult.Delivered, Run(Sink().SendAsync(new[] { Event("a") }, CancellationToken.None)));

			_transport.Respond = request => FakeTransport.Ok(request, 200, "not json");
			LogAssert.Expect(LogType.Warning, new Regex("not valid JSON"));
			Assert.AreEqual(AnalyticsSendResult.Delivered, Run(Sink().SendAsync(new[] { Event("a") }, CancellationToken.None)));
		}

		private static AnalyticsEvent Big(string name, int length)
		{
			return new AnalyticsEvent(Guid.NewGuid().ToString("N"), name, new[] { new AnalyticsParameter("s", AnalyticsValue.String(new string('x', length))) },
				DateTime.UtcNow, false, 1, null, null, null, App);
		}

		private List<string> SentNames()
		{
			var names = new List<string>();

			foreach (var request in _transport.Requests)
			{
				Assert.IsTrue(AnalyticsJson.TryDeserializeBatch(request.Body, out var sent, out var error), error);
				sent.ForEach(e => names.Add(e.Name));
			}

			return names;
		}

		[Test]
		public void Send_SplitsBodiesLargerThanTheLimit()
		{
			var events = new[] { Big("a", 300), Big("b", 300), Big("c", 300), Big("d", 300), Big("e", 300) };
			var limit = AnalyticsJson.SerializeBatch(new[] { events[0], events[1] }).Length;
			var sink = Sink(o => o.MaxBodyBytes = limit);

			Assert.AreEqual(AnalyticsSendResult.Delivered, Run(sink.SendAsync(events, CancellationToken.None)));

			CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, SentNames());
			Assert.IsTrue(_transport.Requests.TrueForAll(r => r.Body.Length <= limit));
		}

		[Test]
		public void Send_DropsOnlyAnEventThatIsTooLargeOnItsOwn()
		{
			var limit = AnalyticsJson.SerializeBatch(new[] { Big("small", 10) }).Length + 100;
			var sink = Sink(o => o.MaxBodyBytes = limit);

			LogAssert.Expect(LogType.Error, new Regex("Dropping analytics event huge"));
			Assert.AreEqual(AnalyticsSendResult.Delivered,
				Run(sink.SendAsync(new[] { Big("small", 10), Big("huge", limit), Big("other", 10) }, CancellationToken.None)));

			CollectionAssert.AreEqual(new[] { "small", "other" }, SentNames());
		}

		[Test]
		public void Send_SplitsWhenTheServerReturns413()
		{
			var accepted = new List<string>();
			_transport.Respond = request =>
			{
				AnalyticsJson.TryDeserializeBatch(request.Body, out var sent, out _);

				if (sent.Count > 1)
				{
					return FakeTransport.Ok(request, 413);
				}

				accepted.Add(sent[0].Name);
				return FakeTransport.Ok(request, 200);
			};

			Assert.AreEqual(AnalyticsSendResult.Delivered, Run(Sink().SendAsync(new[] { Event("a"), Event("b"), Event("c") }, CancellationToken.None)));
			CollectionAssert.AreEqual(new[] { "a", "b", "c" }, accepted);

			_transport.Requests.Clear();
			_transport.Respond = request => FakeTransport.Ok(request, 413);
			LogAssert.Expect(LogType.Error, new Regex("Dropping analytics event a .*HTTP 413"));
			Assert.AreEqual(AnalyticsSendResult.Rejected, Run(Sink().SendAsync(new[] { Event("a") }, CancellationToken.None)));
		}

		[Test]
		public void Send_RetriesTheWholeBatchWhenAPartNeedsRetry()
		{
			var calls = 0;
			_transport.Respond = request => FakeTransport.Ok(request, ++calls == 1 ? 413 : calls == 2 ? 200 : 503);

			LogAssert.Expect(LogType.Warning, new Regex("HTTP 503"));
			Assert.AreEqual(AnalyticsSendResult.Retry, Run(Sink().SendAsync(new[] { Event("a"), Event("b") }, CancellationToken.None)));
			Assert.AreEqual(3, _transport.Requests.Count);
		}

		[Test]
		public void Constructor_RejectsInvalidOptions()
		{
			Assert.Throws<ArgumentNullException>(() => new CollectorSink(null, new CollectorSinkOptions { Endpoint = "e" }));
			Assert.Throws<ArgumentNullException>(() => new CollectorSink(_http, null));
			Assert.Throws<ArgumentException>(() => Sink(o => o.Endpoint = " "));
			Assert.Throws<ArgumentException>(() => Sink(o => o.MaxBatchSize = 0));
			Assert.Throws<ArgumentException>(() => Sink(o => o.MaxBatchSize = CollectorSinkOptions.MaxAllowedBatchSize + 1));
			Assert.Throws<ArgumentException>(() => Sink(o => o.BatchDelay = TimeSpan.FromSeconds(-1)));
			Assert.Throws<ArgumentException>(() => Sink(o => o.Timeout = TimeSpan.Zero));
			Assert.Throws<ArgumentException>(() => Sink(o => o.MaxBodyBytes = 0));
		}

		[Test]
		public void Send_WithoutTimeout_LeavesItToTheClient()
		{
			Run(Sink(o => o.Timeout = null).SendAsync(new[] { Event("a") }, CancellationToken.None));

			Assert.IsNull(_transport.Requests[0].Timeout);
		}

		[Test]
		public void WithService_StoresOn503AndSendsAgainLater()
		{
			var directory = Path.Combine(Path.GetTempPath(), "geuneda-analytics-collector-tests", Guid.NewGuid().ToString("N"));
			var monotonic = new FakeClock();
			var delays = new List<UniTaskCompletionSource>();
			var service = new AnalyticsService(new AnalyticsOptions { StorageDirectory = directory }, new MemorySequenceStore(), monotonic,
				(delay, token) =>
				{
					var source = new UniTaskCompletionSource();
					token.Register(() => source.TrySetCanceled());
					delays.Add(source);
					return source.Task;
				}, false);

			try
			{
				var statuses = new Queue<int>(new[] { 503, 200 });
				_transport.Respond = request => FakeTransport.Ok(request, statuses.Dequeue());
				service.AddSink(Sink(o => o.BatchDelay = TimeSpan.Zero));

				LogAssert.Expect(LogType.Warning, new Regex("HTTP 503"));
				service.LogEvent("e1");

				Assert.AreEqual(1, Directory.GetFiles(Path.Combine(directory, "collector"), "*.json").Length);

				monotonic.Elapsed += TimeSpan.FromMinutes(1);
				delays.ToArray()[delays.Count - 1].TrySetResult();

				Assert.AreEqual(2, _transport.Requests.Count);
				Assert.AreEqual(_transport.Requests[0].Body, _transport.Requests[1].Body, "the stored batch is resent unchanged");
				Assert.AreEqual(0, Directory.GetFiles(Path.Combine(directory, "collector"), "*.json").Length);
			}
			finally
			{
				service.Dispose();

				if (Directory.Exists(directory))
				{
					Directory.Delete(directory, true);
				}
			}
		}

		private sealed class FakeTransport : IHttpTransport
		{
			public readonly List<HttpRequest> Requests = new List<HttpRequest>();
			public Func<HttpRequest, Result<HttpResponse>> Respond = request => Ok(request, 200);

			public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
			{
				Requests.Add(request);
				return UniTask.FromResult(Respond(request));
			}

			public static Result<HttpResponse> Ok(HttpRequest request, int statusCode, string body = "")
			{
				return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, statusCode, Encoding.UTF8.GetBytes(body), null));
			}
		}

		private sealed class FakeClock : IMonotonicClock
		{
			public TimeSpan Elapsed { get; set; } = TimeSpan.FromHours(1);
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
