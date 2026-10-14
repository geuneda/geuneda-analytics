using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Geuneda.Analytics.Server
{
	/// <summary>
	/// 받은 이벤트를 저장하는 곳입니다. 호스트가 데이터베이스나 로그 파이프라인으로 구현해 DI 에 등록합니다.
	/// </summary>
	/// <remarks>
	/// 클라이언트는 응답을 못 받으면 같은 이벤트를 다시 보내므로 <see cref="CollectedEvent.Id"/> 가 같은 이벤트는 한 번만 저장해야 합니다
	/// (데이터베이스는 ID 고유 키). 한 배치 안에 같은 ID 가 두 번 올 수도 있습니다. 예외를 던지면 응답이 500 이 되고 클라이언트가 배치 전체를 다시 보냅니다.
	/// </remarks>
	public interface IAnalyticsEventStore
	{
		/// <summary>처음 보는 이벤트만 저장합니다.</summary>
		/// <returns>새로 저장한 수와 이미 있던(중복) 수</returns>
		Task<AnalyticsAppendResult> AppendAsync(IReadOnlyList<CollectedEvent> events, CancellationToken cancellationToken);
	}

	/// <summary><see cref="IAnalyticsEventStore.AppendAsync"/> 의 결과입니다.</summary>
	public readonly struct AnalyticsAppendResult
	{
		public AnalyticsAppendResult(int accepted, int duplicates)
		{
			Accepted = accepted;
			Duplicates = duplicates;
		}

		/// <summary>새로 저장한 이벤트 수</summary>
		public int Accepted { get; }

		/// <summary>이미 저장돼 있어 건너뛴 이벤트 수</summary>
		public int Duplicates { get; }
	}
}
