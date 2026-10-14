using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Analytics.Server
{
	/// <summary>
	/// 메모리 저장소입니다. 샘플과 테스트용이라 크기 제한이 없고 다시 시작하면 사라집니다.
	/// </summary>
	public sealed class InMemoryAnalyticsEventStore : IAnalyticsEventStore
	{
		private readonly object _gate = new object();
		private readonly HashSet<string> _ids = new HashSet<string>();
		private readonly List<CollectedEvent> _events = new List<CollectedEvent>();

		/// <summary>저장한 이벤트(받은 순서)의 복사본입니다.</summary>
		public IReadOnlyList<CollectedEvent> Events
		{
			get
			{
				lock (_gate)
				{
					return _events.ToArray();
				}
			}
		}

		public Task<AnalyticsAppendResult> AppendAsync(IReadOnlyList<CollectedEvent> events, CancellationToken cancellationToken)
		{
			var accepted = 0;
			var duplicates = 0;

			lock (_gate)
			{
				foreach (var collected in events)
				{
					if (_ids.Add(collected.Id))
					{
						_events.Add(collected);
						accepted++;
					}
					else
					{
						duplicates++;
					}
				}
			}

			return Task.FromResult(new AnalyticsAppendResult(accepted, duplicates));
		}
	}
}
