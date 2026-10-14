using System.Linq;
using Geuneda.Analytics.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// 실행: dotnet run --project Server~/samples/Geuneda.Analytics.SampleServer --no-launch-profile -- --urls http://127.0.0.1:5399
// Unity 상호 운용 테스트: GEUNEDA_ANALYTICS_INTEROP_URL=http://127.0.0.1:5399
var builder = WebApplication.CreateBuilder(args);
var store = new InMemoryAnalyticsEventStore();

builder.Services.AddGeunedaAnalytics();
builder.Services.AddSingleton<IAnalyticsEventStore>(store);

var app = builder.Build();
app.MapGeunedaAnalytics("/analytics");

// 샘플 전용: 받은 이벤트 확인(운영 서버에는 두지 않는다)
app.MapGet("/analytics/debug/events", (string id) =>
{
	var found = store.Events.FirstOrDefault(e => e.Id == id);
	return found == null
		? Results.NotFound()
		: Results.Json(new
		{
			found.Id,
			found.Name,
			found.Sequence,
			found.SessionId,
			found.UserId,
			found.InstallId,
			Parameters = found.Parameters.ToDictionary(p => p.Key, p => new { Type = p.Value.GetType().Name, Value = p.Value }),
			found.UserProperties
		});
});

app.Run();
