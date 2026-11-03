using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// sink 하나의 큐와 전송 루프입니다. 아직 보내지 않은 이벤트는 메모리 큐에, 보내지 못한(재시도) 배치와 백그라운드로 갈 때 남은 이벤트는 그 sink 의 파일 저장소에 둡니다.
	/// 저장된 배치를 먼저 보내고(파일은 결과가 나온 뒤에 지움), 실패하면 지수 백오프로 기다립니다. 메인 스레드에서만 씁니다.
	/// </summary>
	/// <remarks>
	/// 백오프는 둘입니다. sink 백오프(<c>_failures</c>)는 sink 가 재시도를 돌려줬을 때 이 sink 로 보내는 것을 멈춥니다.
	/// 저장소 백오프(<c>_storeFailures</c>)는 목록·파일을 읽지 못했을 때 저장된 배치만 건너뛰게 하고, 그동안 메모리 큐는 계속 보냅니다.
	/// </remarks>
	internal sealed class SinkChannel
	{
		/// <summary>sink 가 준비되지 않았을 때 다시 확인하는 간격입니다.</summary>
		public static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromSeconds(1);

		/// <summary>한 프레임에 보내는 배치 수의 상한입니다. 저장된 배치가 많을 때 바로 끝나는 sink(SDK 어댑터)가 한 프레임을 붙잡지 않게 합니다.</summary>
		public const int BatchesPerFrame = 8;

		/// <summary>디스크 쓰기가 실패한 뒤 다음 쓰기(메모리 상한 초과 때)를 시도하기까지의 대기입니다. 이벤트마다 실패하는 동기 쓰기를 반복하지 않게 합니다.</summary>
		public static readonly TimeSpan DiskRetryDelay = TimeSpan.FromSeconds(5);

		/// <summary>저장하지 못해 버린 이벤트를 Warning 으로 알리는 최소 간격입니다.</summary>
		public static readonly TimeSpan DropWarningInterval = TimeSpan.FromSeconds(5);

		private readonly IAnalyticsSink _sink;
		private readonly EventFileStore _store;
		private readonly IMonotonicClock _clock;
		private readonly Func<TimeSpan, CancellationToken, UniTask> _delay;
		private readonly ModuleLogger _logger;
		private readonly int _maxBatchSize;
		private readonly TimeSpan _batchDelay;
		private readonly int _maxQueued;
		private readonly TimeSpan _retryDelay;
		private readonly TimeSpan _maxRetryDelay;
		private readonly System.Random _jitter = new System.Random();
		private readonly List<AnalyticsEvent> _queue = new List<AnalyticsEvent>();
		private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
		private readonly int _threadId;
		private readonly AnalyticsSinks _id;

		private TimeSpan _firstQueuedAt;
		private bool _running;
		private IReadOnlyList<AnalyticsEvent> _inFlight;
		private string _inFlightFile;
		private int _failures;
		private TimeSpan _retryAt;
		private bool _wakePending;
		private int _wakeGeneration;
		private TimeSpan _wakeAt;
		private bool _wakeWanted;
		private TimeSpan _wantedWakeAt;
		private bool _suspended;
		private bool _closed;
		private TimeSpan _diskRetryAt;
		private int _storeFailures;
		private TimeSpan _storeRetryAt;
		private int _dropped;
		private TimeSpan _dropWarningAt;

		/// <param name="threadId">채널 상태를 바꾸는 스레드(서비스를 만든 메인 스레드)</param>
		public SinkChannel(IAnalyticsSink sink, EventFileStore store, IMonotonicClock clock, Func<TimeSpan, CancellationToken, UniTask> delay,
			ModuleLogger logger, int maxQueued, TimeSpan retryDelay, TimeSpan maxRetryDelay, bool suspended, int threadId)
		{
			_sink = sink;
			_store = store;
			_clock = clock;
			_delay = delay;
			_logger = logger;
			_maxBatchSize = sink.MaxBatchSize;
			_batchDelay = sink.BatchDelay;
			_maxQueued = maxQueued;
			_retryDelay = retryDelay;
			_maxRetryDelay = maxRetryDelay;
			_suspended = suspended;
			_threadId = threadId;
			_id = sink.Id;
		}

		/// <summary>등록할 때 읽은 sink 비트입니다(다른 스레드의 예외 처리에서 sink 멤버를 부르지 않도록 보관).</summary>
		public AnalyticsSinks Id => _id;

		public IAnalyticsSink Sink => _sink;

		internal int QueuedCount => _queue.Count;

		internal int StoredCount => _store.Count;

		internal bool IsBackingOff => _failures > 0;

		internal bool IsStoreBackingOff => _storeFailures > 0;

		internal bool IsSending => _inFlight != null;

		public void Enqueue(AnalyticsEvent analyticsEvent)
		{
			if (_queue.Count == 0)
			{
				_firstQueuedAt = _clock.Elapsed;
			}

			_queue.Add(analyticsEvent);

			if (!_suspended && _queue.Count >= _maxQueued)
			{
				Spill(false);
			}

			Pump();

			// 백그라운드로 가는 중에 기록한 이벤트는 바로 보내지 못하면 곧바로 디스크에 둡니다
			if (_suspended)
			{
				Persist();
			}

			EnforceQueueLimit();
		}

		/// <summary>보낼 것이 있으면 전송 루프를 시작합니다. 이미 돌고 있으면 그 루프가 이어서 처리합니다.</summary>
		public void Pump()
		{
			if (_running || _closed)
			{
				return;
			}

			_running = true;
			RunAsync().Forget();
		}

		/// <summary>앱이 백그라운드로 갑니다. 배치 대기 없이 보내기 시작하고, 메모리의 이벤트(보내는 중인 배치 포함)를 디스크에 둡니다.</summary>
		public void Suspend()
		{
			_suspended = true;
			Pump();
			Persist();
		}

		/// <summary>앱이 돌아왔습니다. 백오프를 풀고 저장된 배치부터 다시 보냅니다.</summary>
		public void Resume()
		{
			_suspended = false;
			ResetBackoff();
		}

		/// <summary>네트워크가 다시 연결됐습니다. 백오프 중이면 풀고 바로 다시 보냅니다.</summary>
		public void NotifyNetworkRestored()
		{
			if (_failures > 0)
			{
				ResetBackoff();
			}
		}

		/// <summary>서비스를 닫습니다. 메모리의 이벤트를 디스크에 두고 대기와 보내는 중인 요청을 취소합니다.</summary>
		public void Close()
		{
			if (_closed)
			{
				return;
			}

			_suspended = true;
			Persist();
			_closed = true;
			CancelWake();

			// 토큰에 sink 가 건 취소 콜백의 예외가 다른 채널의 정리를 막지 않게 로그로 남깁니다(콜백은 모두 실행된 뒤 모아서 던져짐)
			try
			{
				_lifetime.Cancel();
			}
			catch (AggregateException exception)
			{
				_logger.Error(exception, $"A cancellation callback of analytics sink {Id} threw");
			}
		}

		private void ResetBackoff()
		{
			_failures = 0;
			CancelWake();
			Pump();
		}

		private async UniTaskVoid RunAsync()
		{
			try
			{
				var sentThisFrame = 0;

				while (!_closed)
				{
					if (sentThisFrame >= BatchesPerFrame)
					{
						sentThisFrame = 0;

						if (await UniTask.Yield(PlayerLoopTiming.Update, _lifetime.Token).SuppressCancellationThrow())
						{
							return;
						}
					}

					if (!TryTakeBatch(out var batch, out var file))
					{
						break;
					}

					sentThisFrame++;
					_inFlight = batch;
					_inFlightFile = file;
					var result = await SendAsync(batch);

					// 보내는 동안 백그라운드로 가면 Persist 가 이 배치를 파일로 써 둡니다
					var inFlightFile = _inFlightFile;
					_inFlight = null;
					_inFlightFile = null;
					Complete(result, batch, inFlightFile);
				}
			}
			finally
			{
				_running = false;
				_inFlight = null;
				_inFlightFile = null;
				ArmWake();
			}
		}

		private bool TryTakeBatch(out IReadOnlyList<AnalyticsEvent> batch, out string file)
		{
			batch = null;
			file = null;
			var now = _clock.Elapsed;

			if (_failures > 0 && now < _retryAt)
			{
				ScheduleWake(_retryAt - now);
				return false;
			}

			// 저장소를 읽지 못하는 동안(저장소 백오프)에는 저장된 배치만 건너뛰고 메모리 큐는 보냅니다
			var storeUsable = _storeFailures == 0 || now >= _storeRetryAt;

			if (!storeUsable)
			{
				// 다른 깨우기(준비 확인 등)가 먼저 와도 저장소를 다시 읽을 때를 놓치지 않게 매번 예약합니다(이른 쪽이 남음)
				ScheduleWake(_storeRetryAt - now);
			}
			else if (!_store.TryLoad())
			{
				StoreFailed(now);
				storeUsable = false;
			}

			if (_queue.Count == 0 && (!storeUsable || _store.Count == 0))
			{
				return false;
			}

			if (!_sink.IsReady)
			{
				ScheduleWake(ReadinessPollInterval);
				return false;
			}

			if (storeUsable && TryTakeStoredBatch(now, out batch, out file))
			{
				return true;
			}

			if (_queue.Count == 0)
			{
				return false;
			}

			if (!_suspended && _queue.Count < _maxBatchSize && _batchDelay > TimeSpan.Zero)
			{
				var due = _firstQueuedAt + _batchDelay;

				if (now < due)
				{
					ScheduleWake(due - now);
					return false;
				}
			}

			var count = Math.Min(_queue.Count, _maxBatchSize);
			batch = _queue.GetRange(0, count);
			_queue.RemoveRange(0, count);
			return true;
		}

		/// <summary>저장된 배치를 오래된 것부터 꺼냅니다. 손상 파일은 건너뛰고, 큰 배치는 나눠 저장하며, 읽지 못하면 저장소 백오프에 들어갑니다.</summary>
		private bool TryTakeStoredBatch(TimeSpan now, out IReadOnlyList<AnalyticsEvent> batch, out string file)
		{
			batch = null;
			file = null;

			while (_store.TryPeekOldest(out var stored))
			{
				switch (_store.TryRead(stored, out var events))
				{
					case EventFileStore.ReadStatus.Ok when events.Count == 0:
						_store.Delete(stored);
						break;
					case EventFileStore.ReadStatus.Ok when events.Count > _maxBatchSize:
						// 이전 실행이 더 큰 배치 크기로 저장한 파일은 지금 크기로 나눠 저장한 뒤 원본을 지웁니다
						if (!_store.TrySplit(stored, events, _maxBatchSize))
						{
							StoreFailed(now);
							return false;
						}

						break;
					case EventFileStore.ReadStatus.Ok:
						_storeFailures = 0;
						batch = events;
						file = stored;
						return true;
					case EventFileStore.ReadStatus.Unavailable:
						StoreFailed(now);
						return false;
				}
			}

			_storeFailures = 0;
			return false;
		}

		/// <summary>저장소를 읽지 못했습니다. 지수 백오프 뒤에 다시 읽도록 깨우기를 예약합니다.</summary>
		private void StoreFailed(TimeSpan now)
		{
			_storeFailures++;
			_storeRetryAt = now + BackoffWait(_storeFailures);
			ScheduleWake(_storeRetryAt - now);
		}

		private async UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> batch)
		{
			var result = AnalyticsSendResult.Rejected;
			Exception failure = null;

			try
			{
				result = await _sink.SendAsync(batch, _lifetime.Token);
			}
			catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
			{
				result = AnalyticsSendResult.Retry;
			}
			catch (Exception exception)
			{
				failure = exception;
			}

			// sink 가 다른 스레드에서 끝냈으면(예외 포함) 메인 스레드로 돌아온 뒤 로그·채널 상태 변경·다음 sink 호출을 합니다(잠금 없는 설계)
			if (Thread.CurrentThread.ManagedThreadId != _threadId)
			{
				await UniTask.SwitchToMainThread();
			}

			if (failure != null)
			{
				// sink 는 바깥 코드(SDK 어댑터)입니다. 예외는 숨기지 않고 Error 로 남기되, 같은 배치로 계속 예외가 나지 않도록 버립니다
				_logger.Error(failure, $"Analytics sink {Id} threw while sending {batch.Count} events; dropping them");
				return AnalyticsSendResult.Rejected;
			}

			return result;
		}

		private void Complete(AnalyticsSendResult result, IReadOnlyList<AnalyticsEvent> batch, string file)
		{
			switch (result)
			{
				case AnalyticsSendResult.Delivered:
					_failures = 0;
					DeleteFile(file);
					break;
				case AnalyticsSendResult.Retry:
					if (file == null && !_store.TryWrite(batch, null, out _))
					{
						// 디스크에 둘 수 없으면 메모리 큐 앞으로 되돌립니다(상한을 넘으면 오래된 것부터 버림)
						_diskRetryAt = _clock.Elapsed + DiskRetryDelay;
						_queue.InsertRange(0, batch);
						EnforceQueueLimit();
					}

					EnterBackoff(_clock.Elapsed);
					break;
				case AnalyticsSendResult.Rejected:
					_failures = 0;
					_logger.Warning($"Analytics sink {Id} rejected {batch.Count} events; they are dropped");
					DeleteFile(file);
					break;
				default:
					_logger.Error($"Analytics sink {Id} returned undefined result {(int) result}; dropping {batch.Count} events");
					DeleteFile(file);
					break;
			}
		}

		private void DeleteFile(string file)
		{
			if (file != null)
			{
				_store.Delete(file);
			}
		}

		private void EnterBackoff(TimeSpan now)
		{
			_failures++;
			_retryAt = now + BackoffWait(_failures);
		}

		/// <summary><paramref name="failures"/> 번째 연속 실패 뒤의 대기입니다. <c>RetryDelay</c> 부터 두 배씩 <c>MaxRetryDelay</c> 까지, 그 50~100% 무작위입니다.</summary>
		private TimeSpan BackoffWait(int failures)
		{
			var ticks = _retryDelay.Ticks;

			for (var i = 1; i < failures && ticks < _maxRetryDelay.Ticks; i++)
			{
				ticks *= 2;
			}

			var cap = Math.Min(ticks, _maxRetryDelay.Ticks);
			// 50~100% 무작위로 기다려 서버가 살아날 때 클라이언트가 한꺼번에 몰리지 않게 합니다
			return TimeSpan.FromTicks((long) (cap * (0.5 + _jitter.NextDouble() * 0.5)));
		}

		/// <summary>
		/// 메모리 큐를 배치 크기로 나눠 디스크에 씁니다. 디스크 오류면 남은 이벤트는 메모리에 두고 <see cref="DiskRetryDelay"/> 동안은 다시 쓰지 않습니다
		/// (<paramref name="force"/> 면 그래도 씀: 백그라운드로 갈 때와 닫을 때).
		/// </summary>
		private void Spill(bool force)
		{
			var now = _clock.Elapsed;

			if (!force && now < _diskRetryAt)
			{
				return;
			}

			while (_queue.Count > 0)
			{
				var count = Math.Min(_queue.Count, _maxBatchSize);

				if (!_store.TryWrite(_queue.GetRange(0, count), _inFlightFile, out _))
				{
					_diskRetryAt = now + DiskRetryDelay;
					return;
				}

				_queue.RemoveRange(0, count);
			}
		}

		/// <summary>보내는 중인 메모리 배치와 큐를 디스크에 둡니다. 보내는 중인 배치는 결과가 나오면 그 파일을 지우거나 둡니다.</summary>
		private void Persist()
		{
			if (_inFlight != null && _inFlightFile == null && _store.TryWrite(_inFlight, null, out var file))
			{
				_inFlightFile = file;
			}

			Spill(true);
		}

		/// <summary>디스크에 옮기지 못해 메모리 큐가 상한을 넘으면 오래된 이벤트부터 버리고 Warning 으로 알립니다.</summary>
		private void EnforceQueueLimit()
		{
			var excess = _queue.Count - _maxQueued;

			if (excess <= 0)
			{
				return;
			}

			_queue.RemoveRange(0, excess);
			_dropped += excess;
			var now = _clock.Elapsed;

			if (now < _dropWarningAt)
			{
				return;
			}

			_logger.Warning($"Dropped {_dropped} analytics events for sink {Id} because they could not be stored (memory limit {_maxQueued})");
			_dropped = 0;
			_dropWarningAt = now + DropWarningInterval;
		}

		/// <summary>이번 전송 루프가 끝날 때 깨울 시각을 정합니다(가장 이른 것만 남음). 실제 대기는 <see cref="ArmWake"/> 가 하나만 겁니다.</summary>
		private void ScheduleWake(TimeSpan delay)
		{
			var at = _clock.Elapsed + delay;

			if (!_wakeWanted || at < _wantedWakeAt)
			{
				_wakeWanted = true;
				_wantedWakeAt = at;
			}
		}

		/// <remarks>
		/// 대기는 취소할 수 없어 끝날 때까지 남습니다. 한 루프에서 정한 시각 중 가장 이른 것 하나만 걸고,
		/// 이미 건 대기가 그보다 이르면 새로 걸지 않아 남는 대기가 쌓이지 않습니다.
		/// </remarks>
		private void ArmWake()
		{
			if (!_wakeWanted || _closed)
			{
				_wakeWanted = false;
				return;
			}

			_wakeWanted = false;
			var at = _wantedWakeAt;

			if (_wakePending && _wakeAt <= at)
			{
				return;
			}

			_wakePending = true;
			_wakeAt = at;
			var delay = at - _clock.Elapsed;
			WakeAsync(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ++_wakeGeneration).Forget();
		}

		private void CancelWake()
		{
			_wakeWanted = false;
			_wakePending = false;
			_wakeGeneration++;
		}

		/// <remarks>
		/// 취소 토큰 없이 기다리고 세대 번호로 밀려난 깨우기를 버립니다. 토큰(새 CTS 나 등록)은 깨울 때마다 할당하는데,
		/// 준비되지 않은 sink 는 <see cref="ReadinessPollInterval"/> 마다 깨우므로 그 할당이 계속 쌓입니다.
		/// </remarks>
		private async UniTaskVoid WakeAsync(TimeSpan delay, int generation)
		{
			await _delay(delay, CancellationToken.None);

			if (generation != _wakeGeneration)
			{
				return;
			}

			_wakePending = false;
			Pump();
		}
	}
}
