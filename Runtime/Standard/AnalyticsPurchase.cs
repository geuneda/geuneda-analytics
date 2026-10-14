using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 구매 표준 이벤트(<see cref="AnalyticsStandardEvents.Purchase"/>)의 내용입니다. 기록은 <see cref="AnalyticsStandardEventExtensions.LogPurchase(IAnalyticsService, AnalyticsPurchase, System.Collections.Generic.IReadOnlyDictionary{string, object})"/>,
	/// 어댑터는 <see cref="TryRead"/> 로 이벤트에서 다시 읽습니다.
	/// </summary>
	public readonly struct AnalyticsPurchase
	{
		/// <param name="productId">상품 ID. 비었으면 <see cref="ArgumentException"/></param>
		/// <param name="price">단가. 0 이상의 유한한 수(단가 × 수량도 유한해야 함)</param>
		/// <param name="currency">ISO 4217 통화 코드. 영문 3자(대소문자 무관, 대문자로 저장)</param>
		/// <param name="quantity">수량. 1 이상</param>
		/// <param name="transactionId">스토어 거래 ID. 없으면 null</param>
		/// <param name="productName">상품 이름. 없으면 null</param>
		public AnalyticsPurchase(string productId, double price, string currency, int quantity = 1, string transactionId = null, string productName = null)
		{
			if (string.IsNullOrWhiteSpace(productId))
			{
				throw new ArgumentException("Product id is empty", nameof(productId));
			}

			if (double.IsNaN(price) || double.IsInfinity(price) || price < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(price), price, "Price must be a finite number of zero or more");
			}

			if (quantity < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be at least 1");
			}

			if (double.IsInfinity(price * quantity))
			{
				throw new ArgumentOutOfRangeException(nameof(price), price, "Price times quantity is too large");
			}

			ProductId = productId;
			Price = price;
			Currency = AnalyticsCurrency.Normalize(currency, nameof(currency));
			Quantity = quantity;
			TransactionId = string.IsNullOrEmpty(transactionId) ? null : transactionId;
			ProductName = string.IsNullOrEmpty(productName) ? null : productName;
		}

		public string ProductId { get; }

		/// <summary>단가</summary>
		public double Price { get; }

		public string Currency { get; }

		public int Quantity { get; }

		public string TransactionId { get; }

		public string ProductName { get; }

		/// <summary>단가 × 수량</summary>
		public double Value => Price * Quantity;

		/// <summary>
		/// 구매 표준 이벤트에서 내용을 읽습니다. 이름이 다르거나 필수 파라미터(상품 ID, 단가, 수량, 통화)가 없거나 형식이 맞지 않으면 false 입니다.
		/// </summary>
		public static bool TryRead(AnalyticsEvent analyticsEvent, out AnalyticsPurchase purchase)
		{
			purchase = default;

			if (analyticsEvent == null || analyticsEvent.Name != AnalyticsStandardEvents.Purchase ||
			    !AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.ProductId, out var productId) ||
			    !AnalyticsStandardReader.TryNumber(analyticsEvent, AnalyticsStandardParameters.Price, out var price) ||
			    !AnalyticsStandardReader.TryInteger(analyticsEvent, AnalyticsStandardParameters.Quantity, out var quantity) ||
			    !AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.Currency, out var currency) ||
			    !AnalyticsCurrency.IsValid(currency) || price < 0 || quantity < 1 || quantity > int.MaxValue || double.IsInfinity(price * quantity) ||
			    string.IsNullOrWhiteSpace(productId))
			{
				return false;
			}

			AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.TransactionId, out var transactionId);
			AnalyticsStandardReader.TryString(analyticsEvent, AnalyticsStandardParameters.ProductName, out var productName);
			purchase = new AnalyticsPurchase(productId, price, currency, (int) quantity, transactionId, productName);
			return true;
		}

		/// <summary>이 구매의 표준 파라미터 이름인지(추가 파라미터를 고를 때 씀)</summary>
		public static bool IsStandardParameter(string name)
		{
			return name == AnalyticsStandardParameters.ProductId || name == AnalyticsStandardParameters.ProductName ||
			       name == AnalyticsStandardParameters.Price || name == AnalyticsStandardParameters.Quantity ||
			       name == AnalyticsStandardParameters.Value || name == AnalyticsStandardParameters.Currency ||
			       name == AnalyticsStandardParameters.TransactionId;
		}
	}
}
