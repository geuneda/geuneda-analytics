using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Tests
{
	public sealed class AnalyticsValueTest
	{
		[Test]
		public void FromObject_ConvertsSupportedTypes()
		{
			Assert.AreEqual(AnalyticsValue.String("a"), AnalyticsValue.FromObject("a"));
			Assert.AreEqual(AnalyticsValue.String(""), AnalyticsValue.FromObject(null));
			Assert.AreEqual(AnalyticsValue.Boolean(true), AnalyticsValue.FromObject(true));
			Assert.AreEqual(AnalyticsValue.Integer(-3), AnalyticsValue.FromObject(-3));
			Assert.AreEqual(AnalyticsValue.Integer(7), AnalyticsValue.FromObject((byte) 7));
			Assert.AreEqual(AnalyticsValue.Integer(7), AnalyticsValue.FromObject((sbyte) 7));
			Assert.AreEqual(AnalyticsValue.Integer(7), AnalyticsValue.FromObject((short) 7));
			Assert.AreEqual(AnalyticsValue.Integer(7), AnalyticsValue.FromObject((ushort) 7));
			Assert.AreEqual(AnalyticsValue.Integer(uint.MaxValue), AnalyticsValue.FromObject(uint.MaxValue));
			Assert.AreEqual(AnalyticsValue.Integer(long.MinValue), AnalyticsValue.FromObject(long.MinValue));
			Assert.AreEqual(AnalyticsValue.Integer(long.MaxValue), AnalyticsValue.FromObject((ulong) long.MaxValue));
			Assert.AreEqual(AnalyticsValue.Float(0.5), AnalyticsValue.FromObject(0.5f));
			Assert.AreEqual(AnalyticsValue.Float(1.25), AnalyticsValue.FromObject(1.25m));
			Assert.AreEqual(AnalyticsValue.Float(0.1), AnalyticsValue.FromObject(0.1));
			Assert.AreEqual(AnalyticsValue.Integer(5), AnalyticsValue.FromObject(AnalyticsValue.Integer(5)));
		}

		[Test]
		public void FromObject_RejectsUnsupportedValues()
		{
			Assert.Throws<ArgumentException>(() => AnalyticsValue.FromObject(AnalyticsSinks.Console));
			Assert.Throws<ArgumentException>(() => AnalyticsValue.FromObject(new object()));
			Assert.Throws<ArgumentException>(() => AnalyticsValue.FromObject('c'));
			Assert.Throws<ArgumentException>(() => AnalyticsValue.FromObject(ulong.MaxValue));
			Assert.Throws<ArgumentException>(() => AnalyticsValue.FromObject(double.NaN));
			Assert.Throws<ArgumentException>(() => AnalyticsValue.FromObject(float.PositiveInfinity));
		}

		[Test]
		public void Accessors_RejectOtherTypes()
		{
			var value = AnalyticsValue.Integer(1);

			Assert.AreEqual(1L, value.IntegerValue);
			Assert.Throws<InvalidOperationException>(() => _ = value.StringValue);
			Assert.Throws<InvalidOperationException>(() => _ = value.FloatValue);
			Assert.Throws<InvalidOperationException>(() => _ = value.BooleanValue);
			Assert.AreEqual("", default(AnalyticsValue).StringValue, "default is an empty string");
		}

		[Test]
		public void ToString_IsCultureInvariant()
		{
			var culture = Thread.CurrentThread.CurrentCulture;

			try
			{
				Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

				Assert.AreEqual("1.5", AnalyticsValue.Float(1.5).ToString());
				Assert.AreEqual("0.1", AnalyticsValue.Float(0.1).ToString());
				Assert.AreEqual("-12", AnalyticsValue.Integer(-12).ToString());
				Assert.AreEqual("true", AnalyticsValue.Boolean(true).ToString());
				Assert.AreEqual(1.5, AnalyticsValue.Float(1.5).ToObject());
				Assert.AreEqual(-12L, AnalyticsValue.Integer(-12).ToObject());
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = culture;
			}
		}

		[Test]
		public void Equality_ComparesTypeAndValue()
		{
			Assert.AreNotEqual(AnalyticsValue.Integer(1), AnalyticsValue.Float(1));
			Assert.AreNotEqual(AnalyticsValue.Integer(1), AnalyticsValue.Boolean(true));
			Assert.AreNotEqual(AnalyticsValue.String("1"), AnalyticsValue.Integer(1));
			Assert.IsTrue(AnalyticsValue.String(null) == AnalyticsValue.String(""));
		}
	}
}
