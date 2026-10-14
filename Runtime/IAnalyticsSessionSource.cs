// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트에 붙일 세션 ID 를 주는 곳입니다. account 패키지가 있으면 <c>Geuneda.Analytics.Account.AppSessionAnalytics</c> 를 씁니다.
	/// </summary>
	public interface IAnalyticsSessionSource
	{
		/// <summary>지금 세션 ID. 세션이 없으면 null 입니다. 메인 스레드에서 이벤트마다 읽습니다.</summary>
		string SessionId { get; }
	}
}
