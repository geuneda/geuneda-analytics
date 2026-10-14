using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Tests
{
	public sealed class AnalyticsJsonTest
	{
		private static readonly AnalyticsAppInfo App = new AnalyticsAppInfo("install", "1.2.3", "android", "prod", "Pixel 9", "Android OS 16");

		private static AnalyticsEvent Event(string name, IReadOnlyList<AnalyticsParameter> parameters, IReadOnlyDictionary<string, string> userProperties = null,
			string sessionId = "session", string userId = "user", AnalyticsAppInfo app = null)
		{
			return new AnalyticsEvent(Guid.NewGuid().ToString("N"), name, parameters, new DateTime(2026, 10, 14, 1, 2, 3, 456, DateTimeKind.Utc), true, 42,
				sessionId, userId, userProperties, app ?? App);
		}

		[Test]
		public void RoundTrip_KeepsEveryFieldAndValueType()
		{
			var parameters = new[]
			{
				new AnalyticsParameter("text", AnalyticsValue.String("한글 \"quoted\" \n")),
				new AnalyticsParameter("dateLike", AnalyticsValue.String("2026-10-14T00:00:00Z")),
				new AnalyticsParameter("big", AnalyticsValue.Integer(long.MaxValue)),
				new AnalyticsParameter("negative", AnalyticsValue.Integer(-5)),
				new AnalyticsParameter("fraction", AnalyticsValue.Float(0.1)),
				new AnalyticsParameter("whole", AnalyticsValue.Float(3)),
				new AnalyticsParameter("huge", AnalyticsValue.Float(1e300)),
				new AnalyticsParameter("tiny", AnalyticsValue.Float(5e-324)),
				new AnalyticsParameter("flag", AnalyticsValue.Boolean(false))
			};
			var original = Event("stage_clear", parameters, new Dictionary<string, string> { { "tier", "gold" } });
			var minimal = Event("bare", Array.Empty<AnalyticsParameter>(), sessionId: null, userId: null,
				app: new AnalyticsAppInfo(null, null, null, null, null, null));

			Assert.IsTrue(AnalyticsJson.TryDeserializeBatch(AnalyticsJson.SerializeBatch(new[] { original, minimal }), out var events, out var error), error);

			var read = events[0];
			Assert.AreEqual(original.Id, read.Id);
			Assert.AreEqual("stage_clear", read.Name);
			Assert.AreEqual(original.TimestampUtc, read.TimestampUtc);
			Assert.AreEqual(DateTimeKind.Utc, read.TimestampUtc.Kind);
			Assert.IsTrue(read.IsServerTime);
			Assert.AreEqual(42, read.Sequence);
			Assert.AreEqual("session", read.SessionId);
			Assert.AreEqual("user", read.UserId);
			Assert.AreEqual(App, read.App);
			Assert.AreEqual("gold", read.UserProperties["tier"]);
			Assert.AreEqual(parameters.Length, read.Parameters.Count);

			for (var i = 0; i < parameters.Length; i++)
			{
				Assert.AreEqual(parameters[i].Name, read.Parameters[i].Name);
				Assert.AreEqual(parameters[i].Value, read.Parameters[i].Value, parameters[i].Name);
			}

			var bare = events[1];
			Assert.IsNull(bare.SessionId);
			Assert.IsNull(bare.UserId);
			Assert.IsNull(bare.App.InstallId);
			Assert.AreEqual(0, bare.Parameters.Count);
			Assert.AreEqual(0, bare.UserProperties.Count);
		}

		[Test]
		public void Serialize_WritesTheWireFormat()
		{
			var analyticsEvent = new AnalyticsEvent("abc", "e", new[] { new AnalyticsParameter("n", AnalyticsValue.Integer(1)) },
				new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc), false, 7, null, null, null, new AnalyticsAppInfo("i", "v", "p", "dev", "d", "o"));

			var json = Encoding.UTF8.GetString(AnalyticsJson.SerializeBatch(new[] { analyticsEvent }));

			Assert.AreEqual("{\"v\":1,\"events\":[{\"id\":\"abc\",\"name\":\"e\",\"time\":1000,\"serverTime\":false,\"seq\":7," +
			                "\"installId\":\"i\",\"appVersion\":\"v\",\"platform\":\"p\",\"env\":\"dev\",\"device\":\"d\",\"os\":\"o\",\"params\":{\"n\":1}}]}", json);
		}

		[Test]
		public void Serialize_ReplacesUnpairedSurrogates()
		{
			var analyticsEvent = Event("e", new[] { new AnalyticsParameter("s", AnalyticsValue.String("a\uD800b")) });

			Assert.IsTrue(AnalyticsJson.TryDeserializeBatch(AnalyticsJson.SerializeBatch(new[] { analyticsEvent }), out var events, out var error), error);
			Assert.AreEqual("a�b", events[0].Parameters[0].Value.StringValue);
		}

		[Test]
		public void SharesAppInfoAcrossABatch()
		{
			var json = AnalyticsJson.SerializeBatch(new[] { Event("a", null), Event("b", null) });

			Assert.IsTrue(AnalyticsJson.TryDeserializeBatch(json, out var events, out _));
			Assert.AreSame(events[0].App, events[1].App);
		}

		[TestCase("")]
		[TestCase("[]")]
		[TestCase("{\"v\":2,\"events\":[]}")]
		[TestCase("{\"events\":[]}")]
		[TestCase("{\"v\":1}")]
		[TestCase("{\"v\":1,\"events\":[]} {}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":1,\"serverTime\":false,\"seq\":1}]}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":1,\"serverTime\":false,\"seq\":1,\"params\":{\"x\":[1]}}]}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":1,\"serverTime\":false,\"seq\":1e3,\"params\":{}}]}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":1,\"serverTime\":false,\"seq\":99999999999999999999,\"params\":{}}]}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":1,\"serverTime\":\"no\",\"seq\":1,\"params\":{}}]}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"\",\"name\":\"e\",\"time\":1,\"serverTime\":false,\"seq\":1,\"params\":{}}]}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":999999999999999999,\"serverTime\":false,\"seq\":1,\"params\":{}}]}")]
		[TestCase("{\"v\":1,\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":1,\"serverTime\":false,\"seq\":1,\"params\":{},\"userProps\":{\"k\":null}}]}")]
		public void Deserialize_RejectsMalformedBatches(string json)
		{
			Assert.IsFalse(AnalyticsJson.TryDeserializeBatch(Encoding.UTF8.GetBytes(json), out _, out var error));
			Assert.IsNotEmpty(error);
		}

		[Test]
		public void Deserialize_RejectsInvalidUtf8AndIgnoresUnknownFields()
		{
			Assert.IsFalse(AnalyticsJson.TryDeserializeBatch(new byte[] { 0x7B, 0xFF, 0x7D }, out _, out _));

			const string json = "{\"v\":1,\"extra\":{\"a\":[1,2]},\"events\":[{\"id\":\"a\",\"name\":\"e\",\"time\":1,\"serverTime\":false,\"seq\":1,\"params\":{},\"future\":true}]}";
			Assert.IsTrue(AnalyticsJson.TryDeserializeBatch(Encoding.UTF8.GetBytes(json), out var events, out var error), error);
			Assert.AreEqual(1, events.Count);
		}
	}
}
