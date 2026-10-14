// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary><see cref="IAnalyticsSink.SendAsync"/> 의 결과입니다.</summary>
	public enum AnalyticsSendResult
	{
		/// <summary>받는 곳이 받았습니다. 배치를 지웁니다.</summary>
		Delivered = 0,

		/// <summary>일시적인 실패(연결 실패, 타임아웃, 서버 과부하, 취소)입니다. 저장했다가 백오프 뒤에 이 sink 로만 다시 보냅니다.</summary>
		Retry = 1,

		/// <summary>다시 보내도 받지 않을 배치입니다(형식 오류 등). 버립니다. 이유는 sink 가 로그로 남깁니다.</summary>
		Rejected = 2
	}
}
