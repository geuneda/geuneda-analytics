using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 광고 노출 수익 표준 이벤트(<see cref="AnalyticsStandardEvents.AdImpression"/>)의 내용입니다. 미디에이션 SDK 의 노출 수익 콜백에서 기록합니다
	/// (<see cref="AnalyticsStandardEventExtensions.LogAdImpression(IAnalyticsService, AnalyticsAdImpression, System.Collections.Generic.IReadOnlyDictionary{string, object})"/>).
	/// 어댑터는 <see cref="TryRead"/> 로 이벤트에서 다시 읽습니다.
	/// </summary>
	public readonly struct AnalyticsAdImpression
	{
		/// <param name="platform">광고 플랫폼(미디에이션, 예: <c>AppLovin</c>, <c>LevelPlay</c>). 비었으면 <see cref="ArgumentException"/></param>
		/// <param name="source">광고를 낸 네트워크. 모르면 null</param>
		/// <param name="format">광고 형식(예: <c>rewarded</c>). 모르면 null</param>
		/// <param name="unitName">광고 단위 이름·ID. 모르면 null</param>
		/// <param name="revenue">노출 수익. 0 이상의 유한한 수</param>
		/// <param name="currency">ISO 4217 통화 코드. 영문 3자(대소문자 무관, 대문자로 저장)</param>
		/// <param name="placement">게임 안 광고 위치. 없으면 null</param>
		public AnalyticsAdImpression(string platform, string source, string format, string unitName, double revenue, string currency, string placement = null)
		{
			if (string.IsNullOrWhiteSpace(platform))
			{
				throw new ArgumentException("Ad platform is empty", nameof(platform));
			}

			if (double.IsNaN(revenue) || double.IsInfinity(revenue) || revenue < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(revenue), revenue, "Revenue must be a finite number of zero or more");
			}

			Platform = platform;
			Source = string.IsNullOrEmpty(source) ? null : source;
			Format = string.IsNullOrEmpty(format) ? null : format;
			UnitName = string.IsNullOrEmpty(unitName) ? null : unitName;
			Revenue = revenue;
			Currency = AnalyticsCurrency.Normalize(currency, nameof(currency));
			Placement = string.IsNullOrEmpty(placement) ? null : placement;
		}

		public string Platform { get; }

		public string Source { get; }

		public string Format { get; }

		public string UnitName { get; }

		public double Revenue { get; }

		public string Currency { get; }

		public string Placement { get; }

		/// <summary>광고 노출 수익 표준 이벤트에서 내용을 읽습니다. 이름이 다르거나 필수 파라미터(플랫폼, 금액, 통화)가 없거나 형식이 맞지 않으면 false 입니다.</summary>
		public static bool TryRead(AnalyticsEvent analyticsEvent, out AnalyticsAdImpression impression)
		{
			impression = default;

			if (analyticsEvent == null || analyticsEvent.Name != AnalyticsStandardEvents.AdImpression ||
			    !AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.AdPlatform, out var platform) ||
			    !AnalyticsStandardReader.TryNumber(analyticsEvent, AnalyticsStandardParameters.Value, out var revenue) ||
			    !AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.Currency, out var currency) ||
			    !AnalyticsCurrency.IsValid(currency) || revenue < 0 || string.IsNullOrWhiteSpace(platform))
			{
				return false;
			}

			AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.AdSource, out var source);
			AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.AdFormat, out var format);
			AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.AdUnitName, out var unitName);
			AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.AdPlacement, out var placement);
			impression = new AnalyticsAdImpression(platform, source, format, unitName, revenue, currency, placement);
			return true;
		}

		/// <summary>이 광고 노출의 표준 파라미터 이름인지(추가 파라미터를 고를 때 씀)</summary>
		public static bool IsStandardParameter(string name)
		{
			return name == AnalyticsStandardParameters.AdPlatform || name == AnalyticsStandardParameters.AdSource ||
			       name == AnalyticsStandardParameters.AdFormat || name == AnalyticsStandardParameters.AdUnitName ||
			       name == AnalyticsStandardParameters.AdPlacement || name == AnalyticsStandardParameters.Value ||
			       name == AnalyticsStandardParameters.Currency;
		}
	}
}
