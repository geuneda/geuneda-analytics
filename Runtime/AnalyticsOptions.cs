using System;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary><see cref="AnalyticsService"/> 설정입니다.</summary>
	public sealed class AnalyticsOptions
	{
		/// <summary><see cref="RetryDelay"/> 의 최솟값 100ms 입니다.</summary>
		public static readonly TimeSpan MinRetryDelay = TimeSpan.FromMilliseconds(100);

		/// <summary>설치 ID 를 읽을 곳입니다. null 이면 이벤트의 설치 ID 가 비어 있습니다.</summary>
		public IAppIdentity Identity { get; set; }

		/// <summary>이벤트 시각을 정하는 시계입니다. null 이면 기기 시계(서버 시각 아님)입니다.</summary>
		public IAnalyticsClock Clock { get; set; }

		/// <summary>이벤트에 붙일 세션 ID 를 주는 곳입니다. null 이면 세션 ID 가 비어 있습니다.</summary>
		public IAnalyticsSessionSource Session { get; set; }

		/// <summary>sink 를 고르지 않은 이벤트가 가는 곳입니다. 기본은 등록된 모든 sink 입니다.</summary>
		public AnalyticsSinks DefaultSinks { get; set; } = AnalyticsSinks.All;

		/// <summary>
		/// 보내지 못한 이벤트를 저장할 폴더입니다. sink 마다 하위 폴더를 만듭니다. null 이면 <c>Application.persistentDataPath/geuneda-analytics</c> 입니다.
		/// 다른 용도의 파일을 두지 않습니다.
		/// </summary>
		public string StorageDirectory { get; set; }

		/// <summary>sink 마다 저장해 두는 배치 파일 수의 상한입니다. 넘으면 가장 오래된 파일을 지웁니다. 기본 500, 최소 1입니다.</summary>
		public int MaxStoredBatchesPerSink { get; set; } = 500;

		/// <summary>sink 마다 메모리에 쌓아 두는 이벤트 수의 상한입니다. 넘으면 디스크로 옮깁니다(SDK 초기화가 늦을 때). 기본 1000, 최소 1입니다.</summary>
		public int MaxQueuedEventsPerSink { get; set; } = 1000;

		/// <summary>sink 가 처음 <see cref="AnalyticsSendResult.Retry"/> 를 돌려준 뒤 다시 보내기까지의 대기입니다. 실패할 때마다 두 배가 됩니다. 기본 5초, 최소 100ms 입니다.</summary>
		public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

		/// <summary>재시도 대기의 상한입니다. 기본 5분입니다. 앱이 포그라운드로 돌아오거나 네트워크가 다시 연결되면 바로 다시 보냅니다.</summary>
		public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
	}
}
