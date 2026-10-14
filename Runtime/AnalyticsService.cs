using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// <see cref="IAnalyticsService"/> 의 기본 구현입니다. 이벤트를 기록한 순간의 공통 맥락(시각, 순번, 세션, 사용자, 앱)과 함께 만들어,
	/// 고른 sink 마다 따로 큐에 넣고 보냅니다. 한 sink 가 실패하면 그 sink 의 배치만 디스크에 저장해 그 sink 로만 다시 보냅니다.
	/// </summary>
	/// <remarks>
	/// 다시 보내는 때: 백오프가 끝났을 때(5초부터 두 배씩 5분까지), 앱이 포그라운드로 돌아왔을 때, 네트워크가 다시 연결됐을 때, 다음 실행에서 sink 를 등록할 때.
	/// 앱이 백그라운드로 가거나 끝날 때 메모리의 이벤트를 디스크에 둡니다. 플레이 중에 만들면 숨은 GameObject 로 앱 수명을 받습니다.
	/// 앱이 포그라운드에서 비정상 종료하면 아직 보내지 않은 메모리의 이벤트(자사 수집은 최대 배치 대기 시간만큼)는 사라집니다.
	/// </remarks>
	public sealed class AnalyticsService : IAnalyticsService, IDisposable
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Analytics");
		private static readonly IReadOnlyDictionary<string, string> NoUserProperties = new Dictionary<string, string>();

		private readonly List<SinkChannel> _channels = new List<SinkChannel>();
		private readonly AnalyticsAppInfo _app;
		private readonly IAnalyticsClock _clock;
		private readonly IAnalyticsSessionSource _session;
		private readonly SequenceCounter _sequence;
		private readonly IMonotonicClock _monotonic;
		private readonly Func<TimeSpan, CancellationToken, UniTask> _delay;
		private readonly string _storageDirectory;
		private readonly int _maxStoredBatches;
		private readonly int _maxQueuedEvents;
		private readonly TimeSpan _retryDelay;
		private readonly TimeSpan _maxRetryDelay;
		private readonly int _threadId;
		private readonly IBatchFileSystem _fileSystem;

		private IReadOnlyDictionary<string, string> _userProperties = NoUserProperties;
		private GameObject _lifecycleObject;
		private bool _suspended;
		private bool _paused;
		private bool _disposed;

		/// <summary>서비스를 만듭니다. 메인 스레드에서 부릅니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="options"/> 가 null</exception>
		/// <exception cref="ArgumentOutOfRangeException">설정 값이 범위를 벗어남</exception>
		public AnalyticsService(AnalyticsOptions options)
			: this(options, new PlayerPrefsSequenceStore(Logger), new MonotonicClock(), DefaultDelay, Application.isPlaying)
		{
		}

		internal AnalyticsService(AnalyticsOptions options, ISequenceStore sequenceStore, IMonotonicClock monotonic,
			Func<TimeSpan, CancellationToken, UniTask> delay, bool bindLifecycle, IBatchFileSystem fileSystem = null)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			Validate(options);

			_threadId = Thread.CurrentThread.ManagedThreadId;
			_app = AnalyticsAppInfo.Capture(options.Identity);
			_clock = options.Clock ?? new DeviceAnalyticsClock();
			_session = options.Session;
			_sequence = new SequenceCounter(sequenceStore);
			_monotonic = monotonic;
			_delay = delay;
			_fileSystem = fileSystem ?? new SystemBatchFileSystem();
			DefaultSinks = options.DefaultSinks;
			_storageDirectory = options.StorageDirectory ?? Path.Combine(Application.persistentDataPath, "geuneda-analytics");
			_maxStoredBatches = options.MaxStoredBatchesPerSink;
			_maxQueuedEvents = options.MaxQueuedEventsPerSink;
			_retryDelay = options.RetryDelay;
			_maxRetryDelay = options.MaxRetryDelay;

			if (bindLifecycle)
			{
				_lifecycleObject = new GameObject(nameof(AnalyticsLifecycle)) { hideFlags = HideFlags.HideInHierarchy };
				Object.DontDestroyOnLoad(_lifecycleObject);
				_lifecycleObject.AddComponent<AnalyticsLifecycle>().Bind(this);
			}
		}

		/// <inheritdoc />
		public AnalyticsSinks DefaultSinks { get; }

		/// <inheritdoc />
		public string UserId { get; private set; }

		/// <inheritdoc />
		public IReadOnlyDictionary<string, string> UserProperties => _userProperties;

		internal GameObject LifecycleObject => _lifecycleObject;

		internal IReadOnlyList<SinkChannel> Channels => _channels;

		/// <inheritdoc />
		public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null)
		{
			LogEvent(name, parameters, DefaultSinks);
		}

		/// <inheritdoc />
		public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters, AnalyticsSinks sinks)
		{
			ThrowIfUnusable();

			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("Event name is empty", nameof(name));
			}

			if (sinks == AnalyticsSinks.None)
			{
				throw new ArgumentException("No sinks selected", nameof(sinks));
			}

			var converted = ConvertParameters(parameters);
			var timestamp = _clock.UtcNow;

			if (timestamp.Kind == DateTimeKind.Local)
			{
				timestamp = timestamp.ToUniversalTime();
			}

			var analyticsEvent = new AnalyticsEvent(Guid.NewGuid().ToString("N"), name, converted, timestamp, _clock.IsServerTime,
				_sequence.Next(), _session?.SessionId, UserId, _userProperties, _app);

			foreach (var channel in _channels)
			{
				if ((channel.Id & sinks) != 0)
				{
					channel.Enqueue(analyticsEvent);
				}
			}
		}

		/// <inheritdoc />
		public void SetUserId(string userId)
		{
			ThrowIfUnusable();
			UserId = string.IsNullOrEmpty(userId) ? null : userId;

			foreach (var channel in _channels)
			{
				NotifyUserId(channel.Sink, UserId);
			}
		}

		/// <inheritdoc />
		public void SetUserProperty(string name, string value)
		{
			ThrowIfUnusable();

			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentException("User property name is empty", nameof(name));
			}

			// 이벤트가 기록할 때의 사전을 그대로 쥐므로 바꿀 때마다 새 사전을 만듭니다
			var next = new Dictionary<string, string>(_userProperties.Count + 1, StringComparer.Ordinal);

			foreach (var property in _userProperties)
			{
				next[property.Key] = property.Value;
			}

			if (value == null)
			{
				next.Remove(name);
			}
			else
			{
				next[name] = value;
			}

			_userProperties = next;

			foreach (var channel in _channels)
			{
				NotifyUserProperty(channel.Sink, name, value);
			}
		}

		/// <inheritdoc />
		public void AddSink(IAnalyticsSink sink)
		{
			ThrowIfUnusable();

			if (sink == null)
			{
				throw new ArgumentNullException(nameof(sink));
			}

			var id = (int) sink.Id;

			if (id == 0 || (id & (id - 1)) != 0)
			{
				throw new ArgumentException($"Sink id must be exactly one bit, but was {sink.Id}", nameof(sink));
			}

			if (sink.MaxBatchSize < 1)
			{
				throw new ArgumentException($"Sink {sink.Id} MaxBatchSize must be at least 1, but was {sink.MaxBatchSize}", nameof(sink));
			}

			if (sink.BatchDelay < TimeSpan.Zero)
			{
				throw new ArgumentException($"Sink {sink.Id} BatchDelay must not be negative", nameof(sink));
			}

			foreach (var channel in _channels)
			{
				if (channel.Id == sink.Id)
				{
					throw new InvalidOperationException($"A sink for {sink.Id} is already added");
				}
			}

			// 해제된 상태(null)도 알려 이전 사용자 상태가 남은 sink 를 맞춥니다
			NotifyUserId(sink, UserId);

			foreach (var property in _userProperties)
			{
				NotifyUserProperty(sink, property.Key, property.Value);
			}

			var store = new EventFileStore(Path.Combine(_storageDirectory, sink.Id.ToString().ToLowerInvariant()), _maxStoredBatches, Logger, _fileSystem);
			var added = new SinkChannel(sink, store, _monotonic, _delay, Logger, _maxQueuedEvents, _retryDelay, _maxRetryDelay, _suspended, _threadId);
			_channels.Add(added);
			added.Pump();
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			DestroyLifecycle();

			foreach (var channel in _channels)
			{
				channel.Close();
			}
		}

		/// <summary>앱 일시정지·복귀입니다. 일시정지를 먼저 받은 복귀만 복귀로 봅니다(시작할 때 오는 복귀 메시지 무시).</summary>
		internal void HandlePause(bool paused)
		{
			if (_disposed)
			{
				return;
			}

			if (paused)
			{
				_paused = true;
				Suspend();
				return;
			}

			if (!_paused)
			{
				return;
			}

			_paused = false;
			_suspended = false;

			foreach (var channel in _channels)
			{
				channel.Resume();
			}
		}

		/// <summary>앱이 끝납니다. 이후 이벤트는 기록하자마자 디스크에 둡니다.</summary>
		internal void HandleQuit()
		{
			if (!_disposed)
			{
				Suspend();
			}
		}

		internal void HandleNetworkRestored()
		{
			if (_disposed)
			{
				return;
			}

			foreach (var channel in _channels)
			{
				channel.NotifyNetworkRestored();
			}
		}

		private void Suspend()
		{
			_suspended = true;

			foreach (var channel in _channels)
			{
				channel.Suspend();
			}
		}

		private void ThrowIfUnusable()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(AnalyticsService));
			}

			if (Thread.CurrentThread.ManagedThreadId != _threadId)
			{
				throw new InvalidOperationException("AnalyticsService must be used on the thread that created it (the main thread)");
			}
		}

		private static IReadOnlyList<AnalyticsParameter> ConvertParameters(IReadOnlyDictionary<string, object> parameters)
		{
			if (parameters == null || parameters.Count == 0)
			{
				return Array.Empty<AnalyticsParameter>();
			}

			var converted = new AnalyticsParameter[parameters.Count];
			var index = 0;

			foreach (var parameter in parameters)
			{
				if (string.IsNullOrWhiteSpace(parameter.Key))
				{
					throw new ArgumentException("Parameter name is empty", nameof(parameters));
				}

				AnalyticsValue value;

				try
				{
					value = AnalyticsValue.FromObject(parameter.Value);
				}
				catch (ArgumentException exception)
				{
					throw new ArgumentException($"Parameter '{parameter.Key}': {exception.Message}", nameof(parameters), exception);
				}

				converted[index++] = new AnalyticsParameter(parameter.Key, value);
			}

			return converted;
		}

		private static void NotifyUserId(IAnalyticsSink sink, string userId)
		{
			// sink 는 바깥 코드(SDK 어댑터)라 한 sink 의 예외가 다른 sink 와 호출자를 막지 않게 Error 로 남기고 계속합니다
			try
			{
				sink.SetUserId(userId);
			}
			catch (Exception exception)
			{
				Logger.Error(exception, $"Analytics sink {sink.Id} threw in SetUserId");
			}
		}

		private static void NotifyUserProperty(IAnalyticsSink sink, string name, string value)
		{
			try
			{
				sink.SetUserProperty(name, value);
			}
			catch (Exception exception)
			{
				Logger.Error(exception, $"Analytics sink {sink.Id} threw in SetUserProperty({name})");
			}
		}

		private static void Validate(AnalyticsOptions options)
		{
			if (options.MaxStoredBatchesPerSink < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(options), options.MaxStoredBatchesPerSink, "MaxStoredBatchesPerSink must be at least 1");
			}

			if (options.MaxQueuedEventsPerSink < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(options), options.MaxQueuedEventsPerSink, "MaxQueuedEventsPerSink must be at least 1");
			}

			if (options.RetryDelay < AnalyticsOptions.MinRetryDelay)
			{
				throw new ArgumentOutOfRangeException(nameof(options), options.RetryDelay, $"RetryDelay must be at least {AnalyticsOptions.MinRetryDelay}");
			}

			if (options.MaxRetryDelay < options.RetryDelay)
			{
				throw new ArgumentOutOfRangeException(nameof(options), options.MaxRetryDelay, "MaxRetryDelay must not be less than RetryDelay");
			}
		}

		private void DestroyLifecycle()
		{
			var lifecycleObject = _lifecycleObject;
			_lifecycleObject = null;

			if (lifecycleObject == null)
			{
				return;
			}

			// 파괴는 프레임 끝에 일어나므로 먼저 끊어 늦게 오는 메시지가 닫힌 서비스에 닿지 않게 합니다
			if (lifecycleObject.TryGetComponent<AnalyticsLifecycle>(out var lifecycle))
			{
				lifecycle.Unbind();
			}

			if (Application.isPlaying)
			{
				Object.Destroy(lifecycleObject);
			}
			else
			{
				Object.DestroyImmediate(lifecycleObject);
			}
		}

		private static UniTask DefaultDelay(TimeSpan delay, CancellationToken cancellationToken)
		{
			return UniTask.Delay(delay, DelayType.Realtime, PlayerLoopTiming.Update, cancellationToken);
		}
	}
}
