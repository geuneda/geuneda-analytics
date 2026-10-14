using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geuneda.Analytics.Server.Tests
{
	public sealed class CollectEndpointTest
	{
		/// <summary>Unity 클라이언트(<c>AnalyticsJsonTest.Serialize_WritesTheWireFormat</c>)가 쓰는 것과 같은 문자열입니다.</summary>
		private const string ClientBatch = "{\"v\":1,\"events\":[{\"id\":\"abc\",\"name\":\"e\",\"time\":1000,\"serverTime\":false,\"seq\":7," +
		                                   "\"installId\":\"i\",\"appVersion\":\"v\",\"platform\":\"p\",\"env\":\"dev\",\"device\":\"d\",\"os\":\"o\",\"params\":{\"n\":1}}]}";

		private static string Event(string id, string extra = null, string parameters = "{}")
		{
			return "{\"id\":\"" + id + "\",\"name\":\"e\",\"time\":1760400000000,\"serverTime\":true,\"seq\":1,\"params\":" + parameters +
			       (extra == null ? "" : "," + extra) + "}";
		}

		private static string Batch(params string[] events)
		{
			return "{\"v\":1,\"events\":[" + string.Join(",", events) + "]}";
		}

		[Fact]
		public async Task ClientBatch_IsStoredWithTypedValues()
		{
			await using var server = await AnalyticsTestServer.StartAsync();

			var (status, body) = await server.PostAsync(ClientBatch);

			Assert.Equal(200, status);
			Assert.Equal(1, body.GetProperty("accepted").GetInt32());
			Assert.Equal(0, body.GetProperty("duplicates").GetInt32());
			Assert.Equal(0, body.GetProperty("rejected").GetArrayLength());

			var stored = Assert.Single(server.Memory.Events);
			Assert.Equal("abc", stored.Id);
			Assert.Equal("e", stored.Name);
			Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), stored.Timestamp);
			Assert.False(stored.IsServerTime);
			Assert.Equal(7, stored.Sequence);
			Assert.Null(stored.SessionId);
			Assert.Equal("i", stored.InstallId);
			Assert.Equal("dev", stored.Environment);
			Assert.Equal("o", stored.OperatingSystem);
			Assert.Equal(1L, stored.Parameters["n"]);
			Assert.Empty(stored.UserProperties);
			Assert.Equal(AnalyticsTestServer.Now, stored.ReceivedAt);
		}

		[Fact]
		public async Task Values_KeepTheirJsonTypes()
		{
			await using var server = await AnalyticsTestServer.StartAsync();

			var (status, _) = await server.PostAsync(Batch(Event("a", "\"userProps\":{\"tier\":\"gold\"},\"sessionId\":\"s\",\"userId\":null",
				"{\"s\":\"x\",\"i\":9223372036854775807,\"f\":3.0,\"e\":1E3,\"t\":0.1,\"b\":true}")));

			Assert.Equal(200, status);
			var stored = Assert.Single(server.Memory.Events);
			Assert.Equal("x", stored.Parameters["s"]);
			Assert.Equal(long.MaxValue, stored.Parameters["i"]);
			Assert.Equal(3.0, Assert.IsType<double>(stored.Parameters["f"]));
			Assert.Equal(1000.0, Assert.IsType<double>(stored.Parameters["e"]));
			Assert.Equal(0.1, stored.Parameters["t"]);
			Assert.Equal(true, stored.Parameters["b"]);
			Assert.Equal("gold", stored.UserProperties["tier"]);
			Assert.Equal("s", stored.SessionId);
			Assert.Null(stored.UserId);
		}

		[Fact]
		public async Task ResentEvents_AreCountedAsDuplicates()
		{
			await using var server = await AnalyticsTestServer.StartAsync();
			var batch = Batch(Event("a"), Event("b"), Event("a"));

			var first = await server.PostAsync(batch);
			var second = await server.PostAsync(batch);

			Assert.Equal(2, first.Body.GetProperty("accepted").GetInt32());
			Assert.Equal(1, first.Body.GetProperty("duplicates").GetInt32());
			Assert.Equal(0, second.Body.GetProperty("accepted").GetInt32());
			Assert.Equal(3, second.Body.GetProperty("duplicates").GetInt32());
			Assert.Equal(2, server.Memory.Events.Count);
		}

		[Fact]
		public async Task InvalidEvents_AreRejectedOneByOne()
		{
			await using var server = await AnalyticsTestServer.StartAsync(o =>
			{
				o.MaxNameLength = 5;
				o.MaxParameters = 2;
				o.MaxStringLength = 3;
			});

			var events = new[]
			{
				Event("ok"),
				Event("long", "\"name\":\"toolong\"").Replace("\"name\":\"e\",", ""),
				Event("p1", parameters: "{\"a\":[1]}"),
				Event("p2", parameters: "{\"a\":1,\"b\":2,\"c\":3}"),
				Event("p3", parameters: "{\"a\":\"abcd\"}"),
				Event("p4", parameters: "{\"a\":1,\"a\":2}"),
				Event("p5", parameters: "{\"a\":null}"),
				Event("u1", "\"userProps\":{\"k\":1}"),
				Event("t1").Replace("1760400000000", "1.5"),
				Event("t2").Replace("1760400000000", "999999999999999999"),
				Event("s1", "\"sessionId\":\"abcd\""),
				Event("d1", "\"seq\":2"),
				"{\"name\":\"e\",\"time\":1,\"serverTime\":true,\"seq\":1,\"params\":{}}",
				"{\"id\":\"m1\",\"name\":\"e\",\"time\":1,\"serverTime\":true,\"seq\":1}",
				"{\"id\":\"x1\",\"name\":\"\\uD800\",\"time\":1,\"serverTime\":true,\"seq\":1,\"params\":{}}",
				"5"
			};

			var (status, body) = await server.PostAsync(Batch(events));

			Assert.Equal(200, status);
			Assert.Equal(1, body.GetProperty("accepted").GetInt32());
			var rejected = body.GetProperty("rejected").EnumerateArray()
				.Select(r => (Id: r.GetProperty("id").ValueKind == JsonValueKind.Null ? null : r.GetProperty("id").GetString(), Reason: r.GetProperty("reason").GetString()))
				.ToList();
			Assert.Equal(events.Length - 1, rejected.Count);
			Assert.Contains(rejected, r => r.Id == "long" && r.Reason.Contains("longer than 5"));
			Assert.Contains(rejected, r => r.Id == "p1" && r.Reason.Contains("must be a string, number or boolean"));
			Assert.Contains(rejected, r => r.Id == "p2" && r.Reason.Contains("more than 2 parameters"));
			Assert.Contains(rejected, r => r.Id == "p3" && r.Reason.Contains("longer than 3"));
			Assert.Contains(rejected, r => r.Id == "p4" && r.Reason.Contains("duplicate parameter"));
			Assert.Contains(rejected, r => r.Id == "p5");
			Assert.Contains(rejected, r => r.Id == "u1" && r.Reason.Contains("must be a string"));
			Assert.Contains(rejected, r => r.Id == "t1" && r.Reason.Contains("64-bit integer"));
			Assert.Contains(rejected, r => r.Id == "t2" && r.Reason.Contains("out of range"));
			Assert.Contains(rejected, r => r.Id == "s1" && r.Reason.Contains("longer than 3"));
			Assert.Contains(rejected, r => r.Id == "d1" && r.Reason.Contains("duplicate field"));
			Assert.Contains(rejected, r => r.Id == null && r.Reason.Contains("missing 'id'"));
			Assert.Contains(rejected, r => r.Id == "m1" && r.Reason.Contains("missing 'params'"));
			Assert.Contains(rejected, r => r.Id == "x1" && r.Reason.Contains("invalid string"));
			Assert.Contains(rejected, r => r.Id == null && r.Reason.Contains("must be an object"));
		}

		[Fact]
		public async Task IntegersOutsideInt64_AreRejectedNotRounded()
		{
			await using var server = await AnalyticsTestServer.StartAsync();

			var (status, body) = await server.PostAsync(Batch(
				Event("big", parameters: "{\"i\":9223372036854775808}"),
				Event("small", parameters: "{\"i\":-9223372036854775809}"),
				Event("edge", parameters: "{\"max\":9223372036854775807,\"min\":-9223372036854775808,\"f\":9223372036854775808.0}")));

			Assert.Equal(200, status);
			Assert.Equal(1, body.GetProperty("accepted").GetInt32());
			var reasons = body.GetProperty("rejected").EnumerateArray().Select(r => r.GetProperty("reason").GetString()).ToList();
			Assert.All(reasons, r => Assert.Contains("out of the 64-bit integer range", r));
			var stored = Assert.Single(server.Memory.Events);
			Assert.Equal(long.MaxValue, stored.Parameters["max"]);
			Assert.Equal(long.MinValue, stored.Parameters["min"]);
			Assert.IsType<double>(stored.Parameters["f"]);
		}

		[Theory]
		[InlineData("not json")]
		[InlineData("[]")]
		[InlineData("{\"v\":2,\"events\":[]}")]
		[InlineData("{\"v\":\"1\",\"events\":[]}")]
		[InlineData("{\"v\":1}")]
		[InlineData("{\"v\":1,\"events\":{}}")]
		public async Task MalformedBatches_Are400(string json)
		{
			await using var server = await AnalyticsTestServer.StartAsync();

			var (status, body) = await server.PostAsync(json);

			Assert.Equal(400, status);
			Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
		}

		[Fact]
		public async Task TooManyEventsOrTooLargeBody_Are413()
		{
			await using var server = await AnalyticsTestServer.StartAsync(o =>
			{
				o.MaxEventsPerBatch = 2;
				o.MaxBodyBytes = 300;
			});

			Assert.Equal(413, (await server.PostAsync(Batch("{}", "{}", "{}"))).Status);
			Assert.Equal(413, (await server.PostAsync(Batch(Event(new string('a', 400))))).Status);

			// Content-Length 없이(청크) 보낸 큰 본문도 막습니다
			using var chunked = new StreamContent(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(Batch(Event(new string('b', 400))))));
			chunked.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
			var request = new HttpRequestMessage(HttpMethod.Post, "analytics/events") { Content = chunked };
			request.Headers.TransferEncodingChunked = true;
			using var response = await server.Http.SendAsync(request);
			Assert.Equal(413, (int) response.StatusCode);
			Assert.Empty(server.Memory.Events);
		}

		[Fact]
		public async Task NonJsonContentType_Is415()
		{
			await using var server = await AnalyticsTestServer.StartAsync();

			Assert.Equal(415, (await server.PostAsync(ClientBatch, "text/plain")).Status);
			Assert.Equal(200, (await server.PostAsync(ClientBatch, "application/json; charset=utf-8")).Status);
		}

		[Fact]
		public async Task StoreFailure_Is500SoTheClientRetries()
		{
			await using var server = await AnalyticsTestServer.StartAsync(store: new ThrowingStore());

			Assert.Equal(500, (await server.PostAsync(ClientBatch)).Status);
		}

		[Fact]
		public async Task EmptyBatch_IsAccepted()
		{
			await using var server = await AnalyticsTestServer.StartAsync();

			var (status, body) = await server.PostAsync(Batch());

			Assert.Equal(200, status);
			Assert.Equal(0, body.GetProperty("accepted").GetInt32());
		}

		[Fact]
		public async Task Map_WithoutStoreOrRegistration_Throws()
		{
			var noStore = WebApplication.CreateSlimBuilder();
			noStore.Services.AddGeunedaAnalytics();
			await using (var app = noStore.Build())
			{
				Assert.Throws<InvalidOperationException>(() => app.MapGeunedaAnalytics());
			}

			var noRegistration = WebApplication.CreateSlimBuilder();
			noRegistration.Services.AddSingleton<IAnalyticsEventStore>(new InMemoryAnalyticsEventStore());
			await using (var app = noRegistration.Build())
			{
				Assert.Throws<InvalidOperationException>(() => app.MapGeunedaAnalytics());
			}
		}

		[Fact]
		public async Task InvalidOptions_FailAtStartup()
		{
			await Assert.ThrowsAnyAsync<Exception>(() => AnalyticsTestServer.StartAsync(o => o.MaxEventsPerBatch = 0));
		}

		private sealed class ThrowingStore : IAnalyticsEventStore
		{
			public Task<AnalyticsAppendResult> AppendAsync(IReadOnlyList<CollectedEvent> events, CancellationToken cancellationToken)
			{
				throw new InvalidOperationException("database is down");
			}
		}
	}
}
