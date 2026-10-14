using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Tests
{
	internal sealed class FakeMonotonicClock : IMonotonicClock
	{
		public TimeSpan Elapsed { get; set; } = TimeSpan.FromHours(1);

		public void Advance(TimeSpan delta)
		{
			Elapsed += delta;
		}
	}

	internal sealed class FakeAnalyticsClock : IAnalyticsClock
	{
		public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 14, 1, 2, 3, 456, DateTimeKind.Utc);

		public bool IsServerTime { get; set; }
	}

	internal sealed class FakeSessionSource : IAnalyticsSessionSource
	{
		public string SessionId { get; set; }
	}

	internal sealed class FakeIdentity : IAppIdentity
	{
		public string InstallId { get; set; } = "install-1";

		public string DeviceId { get; set; } = "device-1";
	}

	internal sealed class MemorySequenceStore : ISequenceStore
	{
		public long Stored { get; set; }

		public int Saves { get; private set; }

		public long Load()
		{
			return Stored;
		}

		public void Save(long reservedUpTo)
		{
			Stored = reservedUpTo;
			Saves++;
		}
	}

	/// <summary>대기를 기록하고 테스트가 끝낼 때까지 기다리게 하는 지연입니다. 취소되면 바로 끝납니다.</summary>
	internal sealed class ManualDelay
	{
		private readonly List<(TimeSpan Delay, UniTaskCompletionSource Source)> _pending = new List<(TimeSpan, UniTaskCompletionSource)>();

		public List<TimeSpan> Requested { get; } = new List<TimeSpan>();

		public int PendingCount
		{
			get
			{
				_pending.RemoveAll(entry => entry.Source.Task.Status != UniTaskStatus.Pending);
				return _pending.Count;
			}
		}

		public UniTask Delay(TimeSpan delay, CancellationToken cancellationToken)
		{
			Requested.Add(delay);
			var source = new UniTaskCompletionSource();
			cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
			_pending.Add((delay, source));
			return source.Task;
		}

		/// <summary>기다리는 지연을 모두 끝냅니다. 끝내는 동안 새로 생긴 지연은 그대로 둡니다.</summary>
		public void CompleteAll()
		{
			var pending = _pending.ToArray();
			_pending.Clear();

			foreach (var entry in pending)
			{
				entry.Source.TrySetResult();
			}
		}
	}

	/// <summary>받은 배치와 사용자 정보를 기록하는 sink 입니다. <see cref="Handler"/> 로 결과를 정합니다(기본 받음).</summary>
	internal sealed class RecordingSink : IAnalyticsSink
	{
		public RecordingSink(AnalyticsSinks id, int maxBatchSize = 10, TimeSpan batchDelay = default)
		{
			Id = id;
			MaxBatchSize = maxBatchSize;
			BatchDelay = batchDelay;
		}

		public AnalyticsSinks Id { get; }

		public int MaxBatchSize { get; set; }

		public TimeSpan BatchDelay { get; set; }

		public bool IsReady { get; set; } = true;

		public Func<IReadOnlyList<AnalyticsEvent>, CancellationToken, UniTask<AnalyticsSendResult>> Handler { get; set; } =
			(_, __) => UniTask.FromResult(AnalyticsSendResult.Delivered);

		public List<List<AnalyticsEvent>> Batches { get; } = new List<List<AnalyticsEvent>>();

		public List<AnalyticsEvent> Events
		{
			get
			{
				var all = new List<AnalyticsEvent>();
				Batches.ForEach(all.AddRange);
				return all;
			}
		}

		public List<string> UserIds { get; } = new List<string>();

		public List<(string Name, string Value)> UserProperties { get; } = new List<(string, string)>();

		public CancellationToken LastToken { get; private set; }

		public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
		{
			Batches.Add(new List<AnalyticsEvent>(events));
			LastToken = cancellationToken;
			return Handler(events, cancellationToken);
		}

		public void SetUserId(string userId)
		{
			UserIds.Add(userId);
		}

		public void SetUserProperty(string name, string value)
		{
			UserProperties.Add((name, value));
		}

		/// <summary>결과를 차례로 돌려줍니다. 다 쓰면 받음입니다.</summary>
		public void ReturnInOrder(params AnalyticsSendResult[] results)
		{
			var queue = new Queue<AnalyticsSendResult>(results);
			Handler = (_, __) => UniTask.FromResult(queue.Count > 0 ? queue.Dequeue() : AnalyticsSendResult.Delivered);
		}

		/// <summary>테스트가 <see cref="UniTaskCompletionSource{T}"/> 로 끝낼 때까지 기다립니다. 취소되면 재시도로 끝납니다.</summary>
		public List<UniTaskCompletionSource<AnalyticsSendResult>> Gate()
		{
			var gates = new List<UniTaskCompletionSource<AnalyticsSendResult>>();
			Handler = (_, token) =>
			{
				var source = new UniTaskCompletionSource<AnalyticsSendResult>();
				token.Register(() => source.TrySetResult(AnalyticsSendResult.Retry));
				gates.Add(source);
				return source.Task;
			};
			return gates;
		}
	}

	/// <summary>실제 파일 시스템에 실패를 넣는 대역입니다. 함수가 예외를 돌려주면 그 호출을 그 예외로 실패시킵니다.</summary>
	internal sealed class FaultyFileSystem : IBatchFileSystem
	{
		private readonly SystemBatchFileSystem _inner = new SystemBatchFileSystem();

		public Func<string, string, Exception> FailGetFiles { get; set; } = (_, __) => null;
		public Func<string, Exception> FailRead { get; set; } = _ => null;
		public Func<string, Exception> FailDelete { get; set; } = _ => null;

		public string[] GetFiles(string directory, string pattern)
		{
			var failure = FailGetFiles(directory, pattern);
			return failure == null ? _inner.GetFiles(directory, pattern) : throw failure;
		}

		public byte[] ReadAllBytes(string path)
		{
			var failure = FailRead(path);
			return failure == null ? _inner.ReadAllBytes(path) : throw failure;
		}

		public void WriteAllBytes(string path, byte[] bytes)
		{
			_inner.WriteAllBytes(path, bytes);
		}

		public void Move(string source, string destination)
		{
			_inner.Move(source, destination);
		}

		public void Delete(string path)
		{
			var failure = FailDelete(path);

			if (failure != null)
			{
				throw failure;
			}

			_inner.Delete(path);
		}

		public bool FileExists(string path)
		{
			return _inner.FileExists(path);
		}

		public void CreateDirectory(string path)
		{
			_inner.CreateDirectory(path);
		}
	}

	internal static class TestStorage
	{
		public static string NewDirectory()
		{
			return Path.Combine(Path.GetTempPath(), "geuneda-analytics-tests", Guid.NewGuid().ToString("N"));
		}

		public static void Delete(string directory)
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
			}
		}

		public static string[] Files(string directory, AnalyticsSinks sink)
		{
			var path = Path.Combine(directory, sink.ToString().ToLowerInvariant());
			return Directory.Exists(path) ? Directory.GetFiles(path, "*.json") : Array.Empty<string>();
		}
	}
}
