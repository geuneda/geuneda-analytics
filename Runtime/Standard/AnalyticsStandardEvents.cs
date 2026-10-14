// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 어댑터가 벤더 표준 이벤트로 바꾸는 표준 이벤트 이름입니다. 기록은 <see cref="AnalyticsStandardEventExtensions"/> 로 합니다.
	/// 이름과 파라미터 이름은 Firebase 권장 이벤트를 따릅니다.
	/// </summary>
	public static class AnalyticsStandardEvents
	{
		/// <summary>구매(<see cref="AnalyticsPurchase"/>). Firebase <c>purchase</c>, Airbridge 주문 완료, AppsFlyer <c>af_purchase</c>, Meta 구매로 바뀝니다.</summary>
		public const string Purchase = "purchase";

		/// <summary>광고 노출 수익(<see cref="AnalyticsAdImpression"/>). Firebase <c>ad_impression</c>, Airbridge 광고 노출, AppsFlyer 광고 수익, Meta <c>AdImpression</c> 으로 바뀝니다.</summary>
		public const string AdImpression = "ad_impression";
	}

	/// <summary>표준 이벤트의 파라미터 이름입니다. 추가 파라미터에 같은 이름을 쓰면 <see cref="System.ArgumentException"/> 입니다.</summary>
	public static class AnalyticsStandardParameters
	{
		/// <summary>금액(실수). 구매는 단가 × 수량, 광고는 노출 수익입니다.</summary>
		public const string Value = "value";

		/// <summary>ISO 4217 통화 코드(대문자 3자)</summary>
		public const string Currency = "currency";

		public const string ProductId = "product_id";
		public const string ProductName = "product_name";

		/// <summary>단가(실수)</summary>
		public const string Price = "price";

		/// <summary>수량(정수)</summary>
		public const string Quantity = "quantity";

		/// <summary>스토어 거래 ID</summary>
		public const string TransactionId = "transaction_id";

		/// <summary>광고 플랫폼(미디에이션, 예: <c>AppLovin</c>, <c>LevelPlay</c>)</summary>
		public const string AdPlatform = "ad_platform";

		/// <summary>광고를 낸 네트워크</summary>
		public const string AdSource = "ad_source";

		/// <summary>광고 형식(예: <c>rewarded</c>, <c>interstitial</c>, <c>banner</c>)</summary>
		public const string AdFormat = "ad_format";

		public const string AdUnitName = "ad_unit_name";

		/// <summary>게임 안 광고 위치</summary>
		public const string AdPlacement = "ad_placement";
	}
}
