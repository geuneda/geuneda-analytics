using System;
using System.Collections.Generic;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Tests
{
	public sealed class AnalyticsStandardEventTest
	{
		private string _directory;
		private AnalyticsService _service;
		private RecordingSink _sink;

		[SetUp]
		public void SetUp()
		{
			_directory = TestStorage.NewDirectory();
			_service = new AnalyticsService(new AnalyticsOptions { StorageDirectory = _directory }, new MemorySequenceStore(), new FakeMonotonicClock(),
				new ManualDelay().Delay, false);
			_sink = new RecordingSink(AnalyticsSinks.Console);
			_service.AddSink(_sink);
		}

		[TearDown]
		public void TearDown()
		{
			_service.Dispose();
			TestStorage.Delete(_directory);
		}

		[Test]
		public void LogPurchase_WritesStandardParametersThatReadBack()
		{
			_service.LogPurchase(new AnalyticsPurchase("gem_100", 1.99, "usd", 2, "tx-1", "Gems"), new Dictionary<string, object> { { "player_level", 7 } });

			var logged = _sink.Events[0];
			Assert.AreEqual(AnalyticsStandardEvents.Purchase, logged.Name);
			Assert.IsTrue(logged.TryGetParameter(AnalyticsStandardParameters.Value, out var value));
			Assert.AreEqual(3.98, value.FloatValue, 1e-9);
			Assert.IsTrue(logged.TryGetParameter("player_level", out var level));
			Assert.AreEqual(7L, level.IntegerValue);

			Assert.IsTrue(AnalyticsPurchase.TryRead(logged, out var purchase));
			Assert.AreEqual("gem_100", purchase.ProductId);
			Assert.AreEqual(1.99, purchase.Price);
			Assert.AreEqual("USD", purchase.Currency);
			Assert.AreEqual(2, purchase.Quantity);
			Assert.AreEqual("tx-1", purchase.TransactionId);
			Assert.AreEqual("Gems", purchase.ProductName);
		}

		[Test]
		public void LogPurchase_SurvivesStorageRoundTrip()
		{
			_service.LogPurchase(new AnalyticsPurchase("gem_100", 0.99, "KRW"));

			Assert.IsTrue(AnalyticsJson.TryDeserializeBatch(AnalyticsJson.SerializeBatch(_sink.Events), out var read, out var error), error);
			Assert.IsTrue(AnalyticsPurchase.TryRead(read[0], out var purchase));
			Assert.AreEqual(1, purchase.Quantity);
			Assert.IsNull(purchase.TransactionId);
		}

		[Test]
		public void LogAdImpression_WritesStandardParametersThatReadBack()
		{
			_service.LogAdImpression(new AnalyticsAdImpression("AppLovin", "AdMob", "rewarded", "unit-1", 0.0012, "USD", "shop"), null,
				AnalyticsSinks.Console);

			Assert.IsTrue(AnalyticsAdImpression.TryRead(_sink.Events[0], out var impression));
			Assert.AreEqual("AppLovin", impression.Platform);
			Assert.AreEqual("AdMob", impression.Source);
			Assert.AreEqual("rewarded", impression.Format);
			Assert.AreEqual("unit-1", impression.UnitName);
			Assert.AreEqual(0.0012, impression.Revenue);
			Assert.AreEqual("shop", impression.Placement);
			Assert.IsFalse(AnalyticsPurchase.TryRead(_sink.Events[0], out _));
		}

		[Test]
		public void Constructors_RejectInvalidValues()
		{
			Assert.Throws<ArgumentException>(() => new AnalyticsPurchase(" ", 1, "USD"));
			Assert.Throws<ArgumentOutOfRangeException>(() => new AnalyticsPurchase("p", -1, "USD"));
			Assert.Throws<ArgumentOutOfRangeException>(() => new AnalyticsPurchase("p", double.NaN, "USD"));
			Assert.Throws<ArgumentOutOfRangeException>(() => new AnalyticsPurchase("p", 1, "USD", 0));
			Assert.Throws<ArgumentOutOfRangeException>(() => new AnalyticsPurchase("p", double.MaxValue, "USD", 2));
			Assert.Throws<ArgumentException>(() => new AnalyticsPurchase("p", 1, "US"));
			Assert.Throws<ArgumentException>(() => new AnalyticsPurchase("p", 1, "US1"));
			Assert.Throws<ArgumentException>(() => new AnalyticsPurchase("p", 1, null));
			Assert.Throws<ArgumentException>(() => new AnalyticsAdImpression("", null, null, null, 1, "USD"));
			Assert.Throws<ArgumentOutOfRangeException>(() => new AnalyticsAdImpression("MAX", null, null, null, double.PositiveInfinity, "USD"));
		}

		[Test]
		public void Logging_RejectsDefaultsAndConflictingExtras()
		{
			Assert.Throws<ArgumentException>(() => _service.LogPurchase(default));
			Assert.Throws<ArgumentException>(() => _service.LogAdImpression(default));
			Assert.Throws<ArgumentException>(() => _service.LogPurchase(new AnalyticsPurchase("p", 1, "USD"), new Dictionary<string, object> { { "currency", "KRW" } }));
			Assert.Throws<ArgumentNullException>(() => ((IAnalyticsService) null).LogPurchase(new AnalyticsPurchase("p", 1, "USD")));
		}

		[Test]
		public void TryRead_RejectsIncompleteEvents()
		{
			_service.LogEvent(AnalyticsStandardEvents.Purchase, new Dictionary<string, object> { { "product_id", "p" }, { "price", 1.0 }, { "currency", "USD" } });
			_service.LogEvent(AnalyticsStandardEvents.Purchase,
				new Dictionary<string, object> { { "product_id", "p" }, { "price", "1" }, { "quantity", 1 }, { "currency", "USD" } });
			_service.LogEvent(AnalyticsStandardEvents.Purchase,
				new Dictionary<string, object> { { "product_id", "p" }, { "price", 1 }, { "quantity", 1 }, { "currency", "usd" } });
			_service.LogEvent(AnalyticsStandardEvents.AdImpression, new Dictionary<string, object> { { "value", 1.0 }, { "currency", "USD" } });
			_service.LogEvent(AnalyticsStandardEvents.Purchase,
				new Dictionary<string, object> { { "product_id", "p" }, { "price", double.MaxValue }, { "quantity", 2 }, { "currency", "USD" } });

			Assert.IsFalse(AnalyticsPurchase.TryRead(_sink.Events[0], out _), "quantity missing");
			Assert.IsFalse(AnalyticsPurchase.TryRead(_sink.Events[1], out _), "price is text");
			Assert.IsFalse(AnalyticsPurchase.TryRead(_sink.Events[2], out _), "currency not normalized");
			Assert.IsFalse(AnalyticsAdImpression.TryRead(_sink.Events[3], out _), "platform missing");
			Assert.IsFalse(AnalyticsPurchase.TryRead(_sink.Events[4], out _), "price times quantity overflows");
			Assert.IsFalse(AnalyticsPurchase.TryRead(null, out _));
		}
	}
}
