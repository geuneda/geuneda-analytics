using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Tests
{
	public sealed class AnalyticsServiceTest
	{
		private string _directory;
		private MemorySequenceStore _sequence;
		private FakeMonotonicClock _monotonic;
		private ManualDelay _delay;
		private FakeAnalyticsClock _clock;
		private readonly List<AnalyticsService> _services = new List<AnalyticsService>();

		[SetUp]
		public void SetUp()
		{
			_directory = TestStorage.NewDirectory();
			_sequence = new MemorySequenceStore();
			_monotonic = new FakeMonotonicClock();
			_delay = new ManualDelay();
			_clock = new FakeAnalyticsClock();
		}

		[TearDown]
		public void TearDown()
		{
			foreach (var service in _services)
			{
				service.Dispose();
			}

			_services.Clear();
			TestStorage.Delete(_directory);
		}

		private AnalyticsService Create(AnalyticsOptions options = null, IBatchFileSystem fileSystem = null)
		{
			options = options ?? new AnalyticsOptions();
			options.StorageDirectory = options.StorageDirectory ?? _directory;
			options.Clock = options.Clock ?? _clock;
			var service = new AnalyticsService(options, _sequence, _monotonic, _delay.Delay, false, fileSystem);
			_services.Add(service);
			return service;
		}

		[Test]
		public void LogEvent_SnapshotsContextAtLogTime()
		{
			var session = new FakeSessionSource { SessionId = "s1" };
			var service = Create(new AnalyticsOptions { Identity = new FakeIdentity(), Session = session });
			var sink = new RecordingSink(AnalyticsSinks.Console);
			service.AddSink(sink);
			service.SetUserId("u1");
			service.SetUserProperty("tier", "gold");
			_clock.IsServerTime = true;

			service.LogEvent("first", new Dictionary<string, object> { { "n", 3 } });

			session.SessionId = "s2";
			service.SetUserId("u2");
			service.SetUserProperty("tier", "silver");
			_clock.IsServerTime = false;
			_clock.UtcNow = _clock.UtcNow.AddSeconds(1);
			service.LogEvent("second");

			var first = sink.Events[0];
			var second = sink.Events[1];
			Assert.AreEqual("first", first.Name);
			Assert.AreEqual(1, first.Sequence);
			Assert.AreEqual(2, second.Sequence);
			Assert.AreEqual("s1", first.SessionId);
			Assert.AreEqual("s2", second.SessionId);
			Assert.AreEqual("u1", first.UserId);
			Assert.AreEqual("u2", second.UserId);
			Assert.AreEqual("gold", first.UserProperties["tier"]);
			Assert.AreEqual("silver", second.UserProperties["tier"]);
			Assert.IsTrue(first.IsServerTime);
			Assert.IsFalse(second.IsServerTime);
			Assert.AreEqual(new DateTime(2026, 10, 14, 1, 2, 3, 456, DateTimeKind.Utc), first.TimestampUtc);
			Assert.AreEqual(DateTimeKind.Utc, first.TimestampUtc.Kind);
			Assert.AreEqual("install-1", first.App.InstallId);
			Assert.AreEqual(Application.version, first.App.AppVersion);
			Assert.AreSame(first.App, second.App);
			Assert.AreEqual(3L, first.Parameters[0].Value.IntegerValue);
			Assert.That(first.Id, Does.Match("^[0-9a-f]{32}$"));
			Assert.AreNotEqual(first.Id, second.Id);
		}

		[Test]
		public void LogEvent_GoesOnlyToSelectedRegisteredSinks()
		{
			var service = Create(new AnalyticsOptions { DefaultSinks = AnalyticsSinks.Console | AnalyticsSinks.Collector });
			var console = new RecordingSink(AnalyticsSinks.Console);
			var collector = new RecordingSink(AnalyticsSinks.Collector);
			var firebase = new RecordingSink(AnalyticsSinks.Firebase);
			service.AddSink(console);
			service.AddSink(collector);
			service.AddSink(firebase);

			service.LogEvent("default");
			service.LogEvent("selected", null, AnalyticsSinks.Firebase | AnalyticsSinks.Console | AnalyticsSinks.AppsFlyer);
			service.LogEvent("all", null, AnalyticsSinks.All);

			CollectionAssert.AreEqual(new[] { "default", "selected", "all" }, console.Events.Select(e => e.Name));
			CollectionAssert.AreEqual(new[] { "default", "all" }, collector.Events.Select(e => e.Name));
			CollectionAssert.AreEqual(new[] { "selected", "all" }, firebase.Events.Select(e => e.Name));
			Assert.AreSame(console.Events[2], firebase.Events[1]);
		}

		[Test]
		public void LogEvent_RejectsMisuse()
		{
			var service = Create();

			Assert.Throws<ArgumentException>(() => service.LogEvent(" "));
			Assert.Throws<ArgumentException>(() => service.LogEvent("e", null, AnalyticsSinks.None));
			Assert.Throws<ArgumentException>(() => service.LogEvent("e", new Dictionary<string, object> { { " ", 1 } }));

			var exception = Assert.Throws<ArgumentException>(() => service.LogEvent("e", new Dictionary<string, object> { { "mode", AnalyticsSinks.Console } }));
			StringAssert.Contains("'mode'", exception.Message);
		}

		[Test]
		public void LogEvent_FromAnotherThread_Throws()
		{
			var service = Create();

			var exception = Task.Run(() =>
			{
				try
				{
					service.LogEvent("e");
					return null;
				}
				catch (InvalidOperationException caught)
				{
					return caught;
				}
			}).Result;

			Assert.IsNotNull(exception);
		}

		[Test]
		public void AddSink_RejectsInvalidSinks()
		{
			var service = Create();

			Assert.Throws<ArgumentNullException>(() => service.AddSink(null));
			Assert.Throws<ArgumentException>(() => service.AddSink(new RecordingSink(AnalyticsSinks.None)));
			Assert.Throws<ArgumentException>(() => service.AddSink(new RecordingSink(AnalyticsSinks.Console | AnalyticsSinks.Firebase)));
			Assert.Throws<ArgumentException>(() => service.AddSink(new RecordingSink(AnalyticsSinks.All)));
			Assert.Throws<ArgumentException>(() => service.AddSink(new RecordingSink(AnalyticsSinks.Console, 0)));
			Assert.Throws<ArgumentException>(() => service.AddSink(new RecordingSink(AnalyticsSinks.Console, 1, TimeSpan.FromSeconds(-1))));

			service.AddSink(new RecordingSink(AnalyticsSinks.Custom1));
			Assert.Throws<InvalidOperationException>(() => service.AddSink(new RecordingSink(AnalyticsSinks.Custom1)));
		}

		[Test]
		public void AddSink_ReplaysUserIdAndProperties()
		{
			var service = Create();
			service.SetUserId("u1");
			service.SetUserProperty("a", "1");
			service.SetUserProperty("b", "2");
			service.SetUserProperty("a", null);

			var sink = new RecordingSink(AnalyticsSinks.Firebase);
			service.AddSink(sink);

			CollectionAssert.AreEqual(new[] { "u1" }, sink.UserIds);
			CollectionAssert.AreEqual(new[] { ("b", "2") }, sink.UserProperties);

			service.SetUserId("");
			service.SetUserProperty("b", null);

			Assert.IsNull(service.UserId);
			Assert.AreEqual(0, service.UserProperties.Count);
			Assert.AreEqual(null, sink.UserIds[1]);
			Assert.AreEqual(("b", (string) null), sink.UserProperties[1]);
		}

		[Test]
		public void SinkThrowingInSetUserId_IsLoggedAndOthersStillNotified()
		{
			var service = Create();
			var throwing = new ThrowingUserSink();
			var other = new RecordingSink(AnalyticsSinks.Console);
			service.AddSink(throwing);
			service.AddSink(other);

			LogAssert.Expect(LogType.Error, new Regex("threw in SetUserId"));
			LogAssert.Expect(LogType.Exception, new Regex("boom"));
			service.SetUserId("u1");

			CollectionAssert.AreEqual(new[] { null, "u1" }, other.UserIds);
		}

		[Test]
		public void AddSink_NotifiesAClearedUserIdToo()
		{
			var service = Create();
			var neverSet = new RecordingSink(AnalyticsSinks.Console);
			service.AddSink(neverSet);

			service.SetUserId("u1");
			service.SetUserId(null);
			var cleared = new RecordingSink(AnalyticsSinks.Firebase);
			service.AddSink(cleared);

			CollectionAssert.AreEqual(new string[] { null, "u1", null }, neverSet.UserIds);
			CollectionAssert.AreEqual(new string[] { null }, cleared.UserIds);
		}

		[Test]
		public void NotReadySink_KeepsEventsUntilReady()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);

			service.LogEvent("e1");
			service.LogEvent("e2");

			Assert.AreEqual(0, sink.Batches.Count);
			Assert.AreEqual(SinkChannel.ReadinessPollInterval, _delay.Requested.Last());

			_delay.CompleteAll();
			Assert.AreEqual(0, sink.Batches.Count, "still not ready");

			sink.IsReady = true;
			_delay.CompleteAll();

			Assert.AreEqual(1, sink.Batches.Count);
			CollectionAssert.AreEqual(new[] { "e1", "e2" }, sink.Events.Select(e => e.Name));
		}

		[Test]
		public void Retry_StoresBatchAndResendsOnlyToFailedSink()
		{
			var service = Create();
			var failing = new RecordingSink(AnalyticsSinks.Collector);
			var healthy = new RecordingSink(AnalyticsSinks.Firebase);
			failing.ReturnInOrder(AnalyticsSendResult.Retry);
			service.AddSink(failing);
			service.AddSink(healthy);

			service.LogEvent("e1");

			Assert.AreEqual(1, healthy.Events.Count);
			Assert.AreEqual(1, failing.Events.Count);
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);

			// 백오프가 끝나기 전에는 새 이벤트도 보내지 않습니다
			service.LogEvent("e2");
			Assert.AreEqual(1, failing.Batches.Count);

			_monotonic.Advance(TimeSpan.FromMinutes(1));
			_delay.CompleteAll();

			Assert.AreEqual(3, failing.Batches.Count);
			Assert.AreEqual(failing.Batches[0][0].Id, failing.Batches[1][0].Id, "stored batch is resent first with the same id");
			Assert.AreEqual("e2", failing.Batches[2][0].Name);
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
			Assert.AreEqual(2, healthy.Events.Count);
		}

		[Test]
		public void Retry_BacksOffExponentiallyWithJitterUpToTheCap()
		{
			var service = Create(new AnalyticsOptions { RetryDelay = TimeSpan.FromSeconds(1), MaxRetryDelay = TimeSpan.FromSeconds(4) });
			var sink = new RecordingSink(AnalyticsSinks.Collector) { Handler = (_, __) => Cysharp.Threading.Tasks.UniTask.FromResult(AnalyticsSendResult.Retry) };
			service.AddSink(sink);

			service.LogEvent("e");
			var caps = new[] { 1, 2, 4, 4 };

			foreach (var cap in caps)
			{
				var wait = _delay.Requested.Last();
				Assert.That(wait.TotalSeconds, Is.InRange(cap * 0.5, cap), $"cap {cap}");
				_monotonic.Advance(wait);
				_delay.CompleteAll();
			}

			Assert.AreEqual(caps.Length + 1, sink.Batches.Count);
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length, "the same stored batch is kept, not duplicated");
		}

		[Test]
		public void Rejected_DropsBatch()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector);
			sink.ReturnInOrder(AnalyticsSendResult.Rejected);
			service.AddSink(sink);

			LogAssert.Expect(LogType.Warning, new Regex("rejected 1 events"));
			service.LogEvent("bad");
			service.LogEvent("good");

			Assert.AreEqual(2, sink.Batches.Count);
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
		}

		[Test]
		public void SinkThrowingInSend_IsLoggedAndBatchDropped()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector) { Handler = (_, __) => throw new InvalidOperationException("send boom") };
			service.AddSink(sink);

			LogAssert.Expect(LogType.Error, new Regex("threw while sending 1 events"));
			LogAssert.Expect(LogType.Exception, new Regex("send boom"));
			service.LogEvent("e");

			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
			Assert.IsFalse(service.Channels[0].IsBackingOff);
		}

		[Test]
		public void BatchDelay_WaitsUntilFullOrDue()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector, 3, TimeSpan.FromSeconds(10));
			service.AddSink(sink);

			service.LogEvent("e1");
			service.LogEvent("e2");
			Assert.AreEqual(0, sink.Batches.Count);
			Assert.AreEqual(TimeSpan.FromSeconds(10), _delay.Requested.Last());

			service.LogEvent("e3");
			Assert.AreEqual(1, sink.Batches.Count, "a full batch goes at once");
			Assert.AreEqual(3, sink.Batches[0].Count);

			_monotonic.Advance(TimeSpan.FromSeconds(1));
			service.LogEvent("e4");
			_delay.CompleteAll();
			Assert.AreEqual(1, sink.Batches.Count, "the old wake does not send early");

			_monotonic.Advance(TimeSpan.FromSeconds(10));
			_delay.CompleteAll();
			Assert.AreEqual(2, sink.Batches.Count);
			Assert.AreEqual("e4", sink.Batches[1][0].Name);
		}

		[Test]
		public void Pause_StoresQueuedEventsAndNextRunSendsThem()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);
			service.LogEvent("e1", new Dictionary<string, object> { { "k", 1.5 } });
			var original = service.Channels[0];

			service.HandlePause(true);

			Assert.AreEqual(0, original.QueuedCount);
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);

			// 다음 실행
			service.Dispose();
			var next = Create();
			var nextSink = new RecordingSink(AnalyticsSinks.Firebase);
			next.AddSink(nextSink);

			Assert.AreEqual(1, nextSink.Events.Count);
			Assert.AreEqual("e1", nextSink.Events[0].Name);
			Assert.AreEqual(1.5, nextSink.Events[0].Parameters[0].Value.FloatValue);
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
		}

		[Test]
		public void Pause_SendsWaitingBatchAtOnceAndStoresItWhileInFlight()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector, 50, TimeSpan.FromSeconds(10));
			var gates = sink.Gate();
			service.AddSink(sink);
			service.LogEvent("e1");
			Assert.AreEqual(0, sink.Batches.Count);

			service.HandlePause(true);

			Assert.AreEqual(1, sink.Batches.Count, "pausing sends without waiting for the batch delay");
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length, "the in-flight batch is on disk");

			gates[0].TrySetResult(AnalyticsSendResult.Delivered);

			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
		}

		[Test]
		public void Pause_InFlightBatchThatFailsIsStoredOnce()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector);
			var gates = sink.Gate();
			service.AddSink(sink);
			service.LogEvent("e1");

			service.HandlePause(true);
			gates[0].TrySetResult(AnalyticsSendResult.Retry);

			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
			Assert.IsTrue(service.Channels[0].IsBackingOff);
		}

		[Test]
		public void WhilePaused_NewEventsAreStoredImmediately()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);
			service.HandlePause(true);

			service.LogEvent("during-pause");

			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
			Assert.AreEqual(0, service.Channels[0].QueuedCount);
		}

		[Test]
		public void Resume_RetriesAtOnce()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector);
			sink.ReturnInOrder(AnalyticsSendResult.Retry);
			service.AddSink(sink);
			service.LogEvent("e");

			service.HandlePause(false);
			Assert.AreEqual(1, sink.Batches.Count, "a resume without a pause is ignored");

			service.HandlePause(true);
			service.HandlePause(false);

			Assert.AreEqual(2, sink.Batches.Count);
			Assert.IsFalse(service.Channels[0].IsBackingOff);
		}

		[Test]
		public void NetworkRestored_RetriesAtOnce()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector);
			sink.ReturnInOrder(AnalyticsSendResult.Retry);
			service.AddSink(sink);
			service.LogEvent("e");

			service.HandleNetworkRestored();

			Assert.AreEqual(2, sink.Batches.Count);
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
		}

		[Test]
		public void QueueOverLimit_MovesToDisk()
		{
			var service = Create(new AnalyticsOptions { MaxQueuedEventsPerSink = 3 });
			var sink = new RecordingSink(AnalyticsSinks.Firebase, 2) { IsReady = false };
			service.AddSink(sink);

			service.LogEvent("e1");
			service.LogEvent("e2");
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
			service.LogEvent("e3");

			Assert.AreEqual(2, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length, "split into batches of MaxBatchSize");
			Assert.AreEqual(0, service.Channels[0].QueuedCount);

			sink.IsReady = true;
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "e1", "e2", "e3" }, sink.Events.Select(e => e.Name));
		}

		[Test]
		public void StoredBatchesOverLimit_DropOldest()
		{
			var service = Create(new AnalyticsOptions { MaxQueuedEventsPerSink = 1, MaxStoredBatchesPerSink = 2 });
			var sink = new RecordingSink(AnalyticsSinks.Firebase, 1) { IsReady = false };
			service.AddSink(sink);

			service.LogEvent("e1");
			service.LogEvent("e2");
			LogAssert.Expect(LogType.Warning, new Regex("Dropped 1 oldest stored analytics batches"));
			service.LogEvent("e3");

			sink.IsReady = true;
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "e2", "e3" }, sink.Events.Select(e => e.Name));
		}

		[Test]
		public void CorruptStoredBatch_IsDeletedAndSkipped()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);
			service.LogEvent("e1");
			service.HandlePause(true);
			service.Dispose();

			var folder = Path.Combine(_directory, "firebase");
			File.WriteAllText(Path.Combine(folder, "0000000000000000000-000000.json"), "{\"v\":1,\"events\":[{\"id\":");
			File.WriteAllText(Path.Combine(folder, "0000000000000000001-000000.tmp"), "partial");

			var next = Create();
			var nextSink = new RecordingSink(AnalyticsSinks.Firebase);
			LogAssert.Expect(LogType.Warning, new Regex("Deleting corrupt analytics batch 0000000000000000000-000000.json"));
			next.AddSink(nextSink);

			CollectionAssert.AreEqual(new[] { "e1" }, nextSink.Events.Select(e => e.Name));
			Assert.AreEqual(0, Directory.GetFiles(folder).Length);
		}

		[Test]
		public void Dispose_StoresEventsCancelsInFlightAndRejectsFurtherUse()
		{
			var service = Create();
			var sending = new RecordingSink(AnalyticsSinks.Collector);
			sending.Gate();
			var waiting = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sending);
			service.AddSink(waiting);
			service.LogEvent("e1");

			service.Dispose();

			Assert.IsTrue(sending.LastToken.IsCancellationRequested);
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length, "cancelled in-flight batch is kept");
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
			Assert.AreEqual(0, _delay.PendingCount, "readiness poll is cancelled");
			Assert.Throws<ObjectDisposedException>(() => service.LogEvent("late"));
			Assert.Throws<ObjectDisposedException>(() => service.AddSink(new RecordingSink(AnalyticsSinks.Console)));
			service.Dispose();
		}

		[Test]
		public void Sequence_ContinuesAfterReservedBlockOnNextRun()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Console);
			service.AddSink(sink);
			service.LogEvent("e1");
			service.LogEvent("e2");

			Assert.AreEqual(SequenceCounter.BlockSize, _sequence.Stored);
			Assert.AreEqual(1, _sequence.Saves);

			var next = Create();
			var nextSink = new RecordingSink(AnalyticsSinks.Console);
			next.AddSink(nextSink);
			next.LogEvent("e3");

			Assert.AreEqual(SequenceCounter.BlockSize + 1, nextSink.Events[0].Sequence);
		}

		[Test]
		public void Options_OutOfRange_Throw()
		{
			Assert.Throws<ArgumentNullException>(() => new AnalyticsService(null));
			Assert.Throws<ArgumentOutOfRangeException>(() => Create(new AnalyticsOptions { MaxQueuedEventsPerSink = 0 }));
			Assert.Throws<ArgumentOutOfRangeException>(() => Create(new AnalyticsOptions { MaxStoredBatchesPerSink = 0 }));
			Assert.Throws<ArgumentOutOfRangeException>(() => Create(new AnalyticsOptions { RetryDelay = TimeSpan.FromMilliseconds(99) }));
			Assert.Throws<ArgumentOutOfRangeException>(() => Create(new AnalyticsOptions { RetryDelay = TimeSpan.FromSeconds(2), MaxRetryDelay = TimeSpan.FromSeconds(1) }));
		}

		[Test]
		public void StoredBatches_AreSentAtMostEightPerCall()
		{
			var service = Create(new AnalyticsOptions { MaxQueuedEventsPerSink = 1 });
			var sink = new RecordingSink(AnalyticsSinks.Firebase, 1) { IsReady = false };
			service.AddSink(sink);

			for (var i = 0; i < 10; i++)
			{
				service.LogEvent("e" + i);
			}

			sink.IsReady = true;
			_delay.CompleteAll();

			Assert.AreEqual(SinkChannel.BatchesPerFrame, sink.Batches.Count);
			Assert.IsTrue(service.Channels[0].IsSending || service.Channels[0].StoredCount > 0);
		}

		[Test]
		public void UnreadableStoredBatch_IsRetriedAfterTheBackoffOnItsOwn()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);
			service.LogEvent("e1");
			service.HandlePause(true);
			service.Dispose();

			var next = Create();
			var nextSink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			next.AddSink(nextSink);

			// 목록을 읽은 뒤 파일 자리를 폴더로 바꿔 읽기를 실패시킵니다
			var path = TestStorage.Files(_directory, AnalyticsSinks.Firebase).Single();
			var bytes = File.ReadAllBytes(path);
			File.Delete(path);
			Directory.CreateDirectory(path);
			LogAssert.Expect(LogType.Warning, new Regex("Could not read stored analytics batch"));
			nextSink.IsReady = true;
			_delay.CompleteAll();

			Assert.AreEqual(0, nextSink.Batches.Count);
			Assert.IsTrue(next.Channels[0].IsStoreBackingOff);
			Assert.IsFalse(next.Channels[0].IsBackingOff, "a storage problem does not stop the sink");
			Assert.AreEqual(1, _delay.PendingCount, "a wake-up is scheduled for the end of the storage backoff");

			// 저장소를 못 읽는 동안에도 새 이벤트는 보냅니다
			next.LogEvent("live");
			CollectionAssert.AreEqual(new[] { "live" }, nextSink.Events.Select(e => e.Name));

			Directory.Delete(path);
			File.WriteAllBytes(path, bytes);
			_monotonic.Advance(TimeSpan.FromMinutes(1));
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "live", "e1" }, nextSink.Events.Select(e => e.Name));
		}

		[Test]
		public void TempFileThatCannotBeDeleted_DoesNotHideStoredBatches()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);
			service.LogEvent("e1");
			service.HandlePause(true);
			service.Dispose();

			var folder = Path.Combine(_directory, "firebase");
			File.WriteAllText(Path.Combine(folder, "leftover.tmp"), "partial");
			var fileSystem = new FaultyFileSystem { FailDelete = path => path.EndsWith(".tmp") ? new UnauthorizedAccessException("locked") : null };

			var next = Create(fileSystem: fileSystem);
			var nextSink = new RecordingSink(AnalyticsSinks.Firebase);
			LogAssert.Expect(LogType.Warning, new Regex("Could not delete temporary analytics file .*leftover.tmp"));
			next.AddSink(nextSink);

			CollectionAssert.AreEqual(new[] { "e1" }, nextSink.Events.Select(e => e.Name));
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
		}

		[Test]
		public void ListingFailure_IsRetriedWhileNewEventsStillGoOut()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);
			service.LogEvent("stored");
			service.HandlePause(true);
			service.Dispose();

			var listingFails = true;
			var fileSystem = new FaultyFileSystem
			{
				FailGetFiles = (_, pattern) => listingFails && pattern == "*.json" ? new UnauthorizedAccessException("no access") : null
			};
			var next = Create(fileSystem: fileSystem);
			var nextSink = new RecordingSink(AnalyticsSinks.Firebase);
			LogAssert.Expect(LogType.Warning, new Regex("Could not list stored analytics batches"));
			next.AddSink(nextSink);

			Assert.AreEqual(0, nextSink.Batches.Count);
			Assert.IsTrue(next.Channels[0].IsStoreBackingOff);
			Assert.AreEqual(1, _delay.PendingCount, "the listing is retried on its own");

			next.LogEvent("live");
			CollectionAssert.AreEqual(new[] { "live" }, nextSink.Events.Select(e => e.Name));

			listingFails = false;
			_monotonic.Advance(TimeSpan.FromMinutes(1));
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "live", "stored" }, nextSink.Events.Select(e => e.Name));
			Assert.IsFalse(next.Channels[0].IsStoreBackingOff);
		}

		[Test]
		public void ListingRetry_IsNotLostBehindReadinessPolls()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);
			service.LogEvent("stored");
			service.HandlePause(true);
			service.Dispose();

			var listingFails = true;
			var fileSystem = new FaultyFileSystem
			{
				FailGetFiles = (_, pattern) => listingFails && pattern == "*.json" ? new IOException("busy") : null
			};
			var next = Create(fileSystem: fileSystem);
			var nextSink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			LogAssert.Expect(LogType.Warning, new Regex("Could not list stored analytics batches"));
			next.AddSink(nextSink);

			// 준비 확인 깨우기(1초)가 저장소 재시도 깨우기를 앞질러도 재시도를 잃지 않아야 합니다
			next.LogEvent("live");
			_delay.CompleteAll();
			nextSink.IsReady = true;
			_delay.CompleteAll();
			CollectionAssert.AreEqual(new[] { "live" }, nextSink.Events.Select(e => e.Name));

			listingFails = false;
			_monotonic.Advance(TimeSpan.FromMinutes(1));
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "live", "stored" }, nextSink.Events.Select(e => e.Name));
		}

		[Test]
		public void RecoveredListing_AppliesTheStoredBatchLimit()
		{
			var options = new AnalyticsOptions { MaxStoredBatchesPerSink = 2, MaxQueuedEventsPerSink = 1 };
			var service = Create(options);
			service.AddSink(new RecordingSink(AnalyticsSinks.Firebase, 1) { IsReady = false });
			service.LogEvent("old1");
			service.LogEvent("old2");
			service.Dispose();

			var listingFails = true;
			var fileSystem = new FaultyFileSystem
			{
				FailGetFiles = (_, pattern) => listingFails && pattern == "*.json" ? new IOException("busy") : null
			};
			var next = Create(new AnalyticsOptions { MaxStoredBatchesPerSink = 2, MaxQueuedEventsPerSink = 1 }, fileSystem);
			var sink = new RecordingSink(AnalyticsSinks.Firebase, 1) { IsReady = false };
			LogAssert.Expect(LogType.Warning, new Regex("Could not list stored analytics batches"));
			next.AddSink(sink);
			next.LogEvent("new1");
			next.LogEvent("new2");
			Assert.AreEqual(4, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length, "new batches are stored without the listing");

			listingFails = false;
			_monotonic.Advance(TimeSpan.FromMinutes(1));
			LogAssert.Expect(LogType.Warning, new Regex("Dropped 2 oldest stored analytics batches"));
			_delay.CompleteAll();

			Assert.AreEqual(2, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);

			sink.IsReady = true;
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "new1", "new2" }, sink.Events.Select(e => e.Name));
		}

		[Test]
		public void SplitStoredBatch_RespectsTheStoredBatchLimit()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Collector, 5) { IsReady = false };
			service.AddSink(sink);

			for (var i = 0; i < 5; i++)
			{
				service.LogEvent("e" + i);
			}

			service.HandlePause(true);
			service.Dispose();

			var next = Create(new AnalyticsOptions { MaxStoredBatchesPerSink = 2 });
			var nextSink = new RecordingSink(AnalyticsSinks.Collector, 1);
			nextSink.ReturnInOrder(AnalyticsSendResult.Retry);
			LogAssert.Expect(LogType.Warning, new Regex("Dropped 3 oldest stored analytics batches"));
			next.AddSink(nextSink);

			Assert.AreEqual(2, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
			Assert.AreEqual("e3", nextSink.Batches[0][0].Name);

			_monotonic.Advance(TimeSpan.FromMinutes(1));
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "e3", "e3", "e4" }, nextSink.Events.Select(e => e.Name));
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
		}

		[UnityTest]
		public IEnumerator SinkFailingOnAnotherThread_IsHandledOnTheMainThread()
		{
			var mainThread = Thread.CurrentThread.ManagedThreadId;
			var service = Create();
			var sink = new ThreadRecordingSink();
			service.AddSink(sink);
			service.LogEvent("e1");
			service.LogEvent("e2");
			var idThreadsBefore = sink.IdThreads.Count;

			LogAssert.Expect(LogType.Error, new Regex("threw while sending 1 events"));
			LogAssert.Expect(LogType.Exception, new Regex("worker boom"));
			Task.Run(() => sink.First.TrySetException(new InvalidOperationException("worker boom"))).Wait();

			for (var frame = 0; frame < 100 && sink.SendThreads.Count < 2; frame++)
			{
				yield return null;
			}

			Assert.AreEqual(2, sink.SendThreads.Count, "the next batch is still sent");
			CollectionAssert.AreEqual(new[] { mainThread, mainThread }, sink.SendThreads);
			Assert.IsTrue(sink.IdThreads.Skip(idThreadsBefore).All(id => id == mainThread), "Id is not read on the worker thread");
		}

		[UnityTest]
		public IEnumerator SinkFinishingOnAnotherThread_ContinuesOnTheMainThread()
		{
			var mainThread = Thread.CurrentThread.ManagedThreadId;
			var service = Create();
			var sink = new ThreadRecordingSink();
			service.AddSink(sink);
			service.LogEvent("e1");
			service.LogEvent("e2");

			Task.Run(() => sink.First.TrySetResult(AnalyticsSendResult.Delivered)).Wait();

			for (var frame = 0; frame < 100 && sink.SendThreads.Count < 2; frame++)
			{
				yield return null;
			}

			Assert.AreEqual(2, sink.SendThreads.Count);
			CollectionAssert.AreEqual(new[] { mainThread, mainThread }, sink.SendThreads);
			Assert.IsTrue(sink.ReadyThreads.All(id => id == mainThread));
		}

		[Test]
		public void StoredBatchLargerThanTheCurrentBatchSize_IsSplit()
		{
			var service = Create();
			var sink = new RecordingSink(AnalyticsSinks.Firebase, 5) { IsReady = false };
			service.AddSink(sink);

			for (var i = 0; i < 5; i++)
			{
				service.LogEvent("e" + i);
			}

			service.HandlePause(true);
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
			service.Dispose();

			var next = Create();
			var nextSink = new RecordingSink(AnalyticsSinks.Firebase, 2);
			next.AddSink(nextSink);

			CollectionAssert.AreEqual(new[] { 2, 2, 1 }, nextSink.Batches.Select(b => b.Count));
			CollectionAssert.AreEqual(new[] { "e0", "e1", "e2", "e3", "e4" }, nextSink.Events.Select(e => e.Name));
			Assert.AreEqual(0, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
		}

		[Test]
		public void ThrowingCancellationCallback_DoesNotStopOtherSinksFromBeingStored()
		{
			var service = Create();
			var throwing = new RecordingSink(AnalyticsSinks.Collector)
			{
				Handler = (_, token) =>
				{
					token.Register(() => throw new InvalidOperationException("cancel boom"));
					return new UniTaskCompletionSource<AnalyticsSendResult>().Task;
				}
			};
			var waiting = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(throwing);
			service.AddSink(waiting);
			service.LogEvent("e1");

			LogAssert.Expect(LogType.Error, new Regex("cancellation callback of analytics sink Collector threw"));
			LogAssert.Expect(LogType.Exception, new Regex("cancel boom"));
			service.Dispose();

			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Collector).Length);
			Assert.AreEqual(1, TestStorage.Files(_directory, AnalyticsSinks.Firebase).Length);
			Assert.AreEqual(0, _delay.PendingCount);
		}

		[Test]
		public void DiskFailure_KeepsTheMemoryLimitByDroppingOldestEvents()
		{
			Directory.CreateDirectory(_directory);
			File.WriteAllText(Path.Combine(_directory, "firebase"), "a file where the sink folder should be");
			var service = Create(new AnalyticsOptions { MaxQueuedEventsPerSink = 2 });
			var sink = new RecordingSink(AnalyticsSinks.Firebase) { IsReady = false };
			service.AddSink(sink);

			LogAssert.Expect(LogType.Warning, new Regex("Could not store 2 analytics events"));
			LogAssert.Expect(LogType.Warning, new Regex("Dropped 1 analytics events for sink Firebase"));

			for (var i = 1; i <= 5; i++)
			{
				service.LogEvent("e" + i);
			}

			Assert.AreEqual(2, service.Channels[0].QueuedCount);

			sink.IsReady = true;
			_delay.CompleteAll();

			CollectionAssert.AreEqual(new[] { "e4", "e5" }, sink.Events.Select(e => e.Name));
		}

		private sealed class ThreadRecordingSink : IAnalyticsSink
		{
			public UniTaskCompletionSource<AnalyticsSendResult> First { get; } = new UniTaskCompletionSource<AnalyticsSendResult>();
			public List<int> SendThreads { get; } = new List<int>();
			public List<int> ReadyThreads { get; } = new List<int>();
			public List<int> IdThreads { get; } = new List<int>();

			public AnalyticsSinks Id
			{
				get
				{
					lock (IdThreads)
					{
						IdThreads.Add(Thread.CurrentThread.ManagedThreadId);
					}

					return AnalyticsSinks.Collector;
				}
			}
			public int MaxBatchSize => 1;
			public TimeSpan BatchDelay => TimeSpan.Zero;

			public bool IsReady
			{
				get
				{
					lock (ReadyThreads)
					{
						ReadyThreads.Add(Thread.CurrentThread.ManagedThreadId);
					}

					return true;
				}
			}

			public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
			{
				lock (SendThreads)
				{
					SendThreads.Add(Thread.CurrentThread.ManagedThreadId);
					return SendThreads.Count == 1 ? First.Task : UniTask.FromResult(AnalyticsSendResult.Delivered);
				}
			}

			public void SetUserId(string userId)
			{
			}

			public void SetUserProperty(string name, string value)
			{
			}
		}

		private sealed class ThrowingUserSink : IAnalyticsSink
		{
			public AnalyticsSinks Id => AnalyticsSinks.Firebase;
			public int MaxBatchSize => 1;
			public TimeSpan BatchDelay => TimeSpan.Zero;
			public bool IsReady => true;

			public Cysharp.Threading.Tasks.UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
			{
				return Cysharp.Threading.Tasks.UniTask.FromResult(AnalyticsSendResult.Delivered);
			}

			public void SetUserId(string userId)
			{
				if (userId != null)
				{
					throw new InvalidOperationException("boom");
				}
			}

			public void SetUserProperty(string name, string value)
			{
			}
		}
	}
}
