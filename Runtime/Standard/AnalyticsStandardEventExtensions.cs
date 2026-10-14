using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 표준 이벤트(<see cref="AnalyticsStandardEvents"/>)를 기록합니다. 표준 파라미터에 <paramref name="extra"/> 를 더해 한 이벤트로 기록하고,
	/// 각 어댑터가 벤더 표준 이벤트로 바꿉니다(자사 수집·콘솔에는 그대로 갑니다).
	/// </summary>
	public static class AnalyticsStandardEventExtensions
	{
		/// <summary>구매를 <see cref="IAnalyticsService.DefaultSinks"/> 로 기록합니다. 스토어 결제가 확정되고 서버가 지급한 뒤 한 번만 부릅니다.</summary>
		/// <param name="extra">추가 파라미터(예: 플레이어 레벨). 표준 파라미터 이름과 겹치면 <see cref="ArgumentException"/></param>
		public static void LogPurchase(this IAnalyticsService analytics, AnalyticsPurchase purchase, IReadOnlyDictionary<string, object> extra = null)
		{
			LogPurchase(analytics, purchase, extra, Require(analytics).DefaultSinks);
		}

		/// <summary>구매를 <paramref name="sinks"/> 로 기록합니다.</summary>
		public static void LogPurchase(this IAnalyticsService analytics, AnalyticsPurchase purchase, IReadOnlyDictionary<string, object> extra, AnalyticsSinks sinks)
		{
			Require(analytics);

			if (purchase.ProductId == null)
			{
				throw new ArgumentException("Purchase is default; create it with the constructor", nameof(purchase));
			}

			var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				{ AnalyticsStandardParameters.ProductId, purchase.ProductId },
				{ AnalyticsStandardParameters.Price, purchase.Price },
				{ AnalyticsStandardParameters.Quantity, purchase.Quantity },
				{ AnalyticsStandardParameters.Value, purchase.Value },
				{ AnalyticsStandardParameters.Currency, purchase.Currency }
			};

			Add(parameters, AnalyticsStandardParameters.TransactionId, purchase.TransactionId);
			Add(parameters, AnalyticsStandardParameters.ProductName, purchase.ProductName);
			Merge(parameters, extra, AnalyticsPurchase.IsStandardParameter);
			analytics.LogEvent(AnalyticsStandardEvents.Purchase, parameters, sinks);
		}

		/// <summary>광고 노출 수익을 <see cref="IAnalyticsService.DefaultSinks"/> 로 기록합니다. 미디에이션 SDK 의 노출 수익 콜백마다 부릅니다.</summary>
		/// <param name="extra">추가 파라미터. 표준 파라미터 이름과 겹치면 <see cref="ArgumentException"/></param>
		public static void LogAdImpression(this IAnalyticsService analytics, AnalyticsAdImpression impression, IReadOnlyDictionary<string, object> extra = null)
		{
			LogAdImpression(analytics, impression, extra, Require(analytics).DefaultSinks);
		}

		/// <summary>광고 노출 수익을 <paramref name="sinks"/> 로 기록합니다.</summary>
		public static void LogAdImpression(this IAnalyticsService analytics, AnalyticsAdImpression impression, IReadOnlyDictionary<string, object> extra,
			AnalyticsSinks sinks)
		{
			Require(analytics);

			if (impression.Platform == null)
			{
				throw new ArgumentException("Ad impression is default; create it with the constructor", nameof(impression));
			}

			var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				{ AnalyticsStandardParameters.AdPlatform, impression.Platform },
				{ AnalyticsStandardParameters.Value, impression.Revenue },
				{ AnalyticsStandardParameters.Currency, impression.Currency }
			};

			Add(parameters, AnalyticsStandardParameters.AdSource, impression.Source);
			Add(parameters, AnalyticsStandardParameters.AdFormat, impression.Format);
			Add(parameters, AnalyticsStandardParameters.AdUnitName, impression.UnitName);
			Add(parameters, AnalyticsStandardParameters.AdPlacement, impression.Placement);
			Merge(parameters, extra, AnalyticsAdImpression.IsStandardParameter);
			analytics.LogEvent(AnalyticsStandardEvents.AdImpression, parameters, sinks);
		}

		private static IAnalyticsService Require(IAnalyticsService analytics)
		{
			return analytics ?? throw new ArgumentNullException(nameof(analytics));
		}

		private static void Add(Dictionary<string, object> parameters, string name, string value)
		{
			if (value != null)
			{
				parameters.Add(name, value);
			}
		}

		private static void Merge(Dictionary<string, object> parameters, IReadOnlyDictionary<string, object> extra, Func<string, bool> isStandard)
		{
			if (extra == null)
			{
				return;
			}

			foreach (var parameter in extra)
			{
				if (isStandard(parameter.Key))
				{
					throw new ArgumentException($"Extra parameter '{parameter.Key}' is a standard parameter", nameof(extra));
				}

				parameters.Add(parameter.Key, parameter.Value);
			}
		}
	}
}
