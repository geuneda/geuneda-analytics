using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Geuneda.Analytics.Server.Tests
{
	/// <summary>127.0.0.1 빈 포트에 띄운 수집 서버입니다.</summary>
	internal sealed class AnalyticsTestServer : IAsyncDisposable
	{
		public static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

		private readonly WebApplication _app;

		private AnalyticsTestServer(WebApplication app, IAnalyticsEventStore store, Uri baseAddress)
		{
			_app = app;
			Store = store;
			Http = new HttpClient { BaseAddress = baseAddress };
		}

		public IAnalyticsEventStore Store { get; }

		public InMemoryAnalyticsEventStore Memory => (InMemoryAnalyticsEventStore) Store;

		public HttpClient Http { get; }

		public static async Task<AnalyticsTestServer> StartAsync(Action<AnalyticsServerOptions> configure = null, IAnalyticsEventStore store = null)
		{
			store = store ?? new InMemoryAnalyticsEventStore();
			var builder = WebApplication.CreateSlimBuilder();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Logging.ClearProviders();
			builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
			builder.Services.AddGeunedaAnalytics(configure);
			builder.Services.AddSingleton(store);
			var app = builder.Build();
			app.MapGeunedaAnalytics("/analytics");
			await app.StartAsync();

			var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
			return new AnalyticsTestServer(app, store, new Uri(address + "/"));
		}

		public async Task<(int Status, JsonElement Body)> PostAsync(string json, string contentType = "application/json")
		{
			using (var content = new StringContent(json, Encoding.UTF8))
			{
				content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
				return await SendAsync(content);
			}
		}

		public async Task<(int Status, JsonElement Body)> SendAsync(HttpContent content)
		{
			using (var response = await Http.PostAsync("analytics/events", content))
			{
				var text = await response.Content.ReadAsStringAsync();
				var body = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
				return ((int) response.StatusCode, body);
			}
		}

		public async ValueTask DisposeAsync()
		{
			Http.Dispose();
			await _app.StopAsync();
			await _app.DisposeAsync();
		}
	}
}
