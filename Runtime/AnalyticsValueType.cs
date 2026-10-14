// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary><see cref="AnalyticsValue"/> 의 형식입니다. 저장·전송한 JSON 의 토큰 형식과 같습니다.</summary>
	public enum AnalyticsValueType
	{
		String = 0,

		/// <summary>64비트 정수</summary>
		Integer = 1,

		/// <summary>유한한 64비트 실수</summary>
		Float = 2,

		Boolean = 3
	}
}
