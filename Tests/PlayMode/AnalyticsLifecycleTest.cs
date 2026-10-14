using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Tests
{
	/// <summary>플레이 중에 만들어지는 숨은 수명 컴포넌트가 실제 Unity 메시지로 저장·복귀를 다루는지 확인합니다.</summary>
	public sealed class AnalyticsLifecycleTest
	{
		private string _directory;
		private AnalyticsService _service;

		[SetUp]
		public void SetUp()
		{
			_directory = Path.Combine(Path.GetTempPath(), "geuneda-analytics-playmode", Guid.NewGuid().ToString("N"));
		}

		[TearDown]
		public void TearDown()
		{
			_service?.Dispose();

			if (Directory.Exists(_directory))
			{
				Directory.Delete(_directory, true);
			}
		}

		private AnalyticsService Create()
		{
			// 순번은 메모리에 두어 PlayerPrefs 를 건드리지 않고, 대기는 실제 UniTask 지연을 씁니다
			_service = new AnalyticsService(new AnalyticsOptions { StorageDirectory = _directory }, new MemorySequenceStore(), new MonotonicClock(),
				(delay, token) => UniTask.Delay(delay, DelayType.Realtime, PlayerLoopTiming.Update, token), true);
			return _service;
		}

		private int StoredFiles(AnalyticsSinks sink)
		{
			var path = Path.Combine(_directory, sink.ToString().ToLowerInvariant());
			return Directory.Exists(path) ? Directory.GetFiles(path, "*.json").Length : 0;
		}

		[UnityTest]
		public IEnumerator Lifecycle_IsAHiddenObjectThatSurvivesSceneLoads()
		{
			var service = Create();
			yield return null;

			var lifecycle = service.LifecycleObject;
			Assert.IsNotNull(lifecycle);
			Assert.AreEqual(HideFlags.HideInHierarchy, lifecycle.hideFlags);
			Assert.AreEqual("DontDestroyOnLoad", lifecycle.scene.name);

			service.Dispose();
			yield return null;

			Assert.IsTrue(lifecycle == null, "Dispose destroys the hidden object");
		}

		[UnityTest]
		public IEnumerator PauseMessage_StoresEventsAndResumeSendsThem()
		{
			var service = Create();
			var sink = new CountingSink { Ready = false };
			service.AddSink(sink);
			service.LogEvent("e1");

			service.LifecycleObject.SendMessage("OnApplicationPause", true);
			Assert.AreEqual(1, StoredFiles(AnalyticsSinks.Firebase));

			sink.Ready = true;
			service.LifecycleObject.SendMessage("OnApplicationPause", false);
			yield return null;

			Assert.AreEqual(1, sink.Events);
			Assert.AreEqual(0, StoredFiles(AnalyticsSinks.Firebase));
		}

		[UnityTest]
		public IEnumerator QuitMessage_StoresLaterEventsImmediately()
		{
			var service = Create();
			var sink = new CountingSink { Ready = false };
			service.AddSink(sink);

			service.LifecycleObject.SendMessage("OnApplicationQuit");
			service.LogEvent("after-quit");
			yield return null;

			Assert.AreEqual(1, StoredFiles(AnalyticsSinks.Firebase));
		}

		[UnityTest]
		public IEnumerator ManyStoredBatches_AreSentOverSeveralFrames()
		{
			var service = Create();
			var sink = new CountingSink { Ready = false };
			service.AddSink(sink);
			service.LifecycleObject.SendMessage("OnApplicationPause", true);

			for (var i = 0; i < 20; i++)
			{
				service.LogEvent("e" + i);
			}

			Assert.AreEqual(20, StoredFiles(AnalyticsSinks.Firebase));

			sink.Ready = true;
			service.LifecycleObject.SendMessage("OnApplicationPause", false);
			Assert.AreEqual(SinkChannel.BatchesPerFrame, sink.Events, "one frame sends at most BatchesPerFrame batches");

			for (var frame = 0; frame < 10 && sink.Events < 20; frame++)
			{
				yield return null;
			}

			Assert.AreEqual(20, sink.Events);
			Assert.AreEqual(0, StoredFiles(AnalyticsSinks.Firebase));
		}

		private sealed class CountingSink : IAnalyticsSink
		{
			public bool Ready { get; set; } = true;
			public int Events { get; private set; }

			public AnalyticsSinks Id => AnalyticsSinks.Firebase;
			public int MaxBatchSize => 1;
			public TimeSpan BatchDelay => TimeSpan.Zero;
			public bool IsReady => Ready;

			public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
			{
				Events += events.Count;
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
