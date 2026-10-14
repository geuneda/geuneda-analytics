using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geuneda.Analytics.Server
{
	/// <summary>수집 서버 등록 확장입니다.</summary>
	public static class AnalyticsServerExtensions
	{
		/// <summary>
		/// 수집 엔드포인트가 쓰는 설정과 시계를 등록합니다. 저장소(<see cref="IAnalyticsEventStore"/>)는 호스트가 따로 등록합니다.
		/// 설정 값이 1 보다 작으면 시작할 때 실패합니다.
		/// </summary>
		public static IServiceCollection AddGeunedaAnalytics(this IServiceCollection services, Action<AnalyticsServerOptions> configure = null)
		{
			if (services == null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			var options = services.AddOptions<AnalyticsServerOptions>();

			if (configure != null)
			{
				options.Configure(configure);
			}

			options.Validate(o => o.MaxBodyBytes >= 1 && o.MaxEventsPerBatch >= 1 && o.MaxNameLength >= 1 && o.MaxParameters >= 1 && o.MaxStringLength >= 1,
					"AnalyticsServerOptions values must be at least 1")
				.ValidateOnStart();
			services.TryAddSingleton(TimeProvider.System);
			return services;
		}

		/// <summary>
		/// <c>POST {prefix}/events</c> 수집 엔드포인트를 붙입니다. 인증·요청 수 제한은 돌려준 빌더에 <c>RequireAuthorization</c>, <c>RequireRateLimiting</c> 으로 붙입니다.
		/// </summary>
		/// <exception cref="InvalidOperationException"><see cref="IAnalyticsEventStore"/> 가 등록되지 않음</exception>
		public static RouteHandlerBuilder MapGeunedaAnalytics(this IEndpointRouteBuilder endpoints, string prefix = "/analytics")
		{
			if (endpoints == null)
			{
				throw new ArgumentNullException(nameof(endpoints));
			}

			if (prefix == null)
			{
				throw new ArgumentNullException(nameof(prefix));
			}

			var registry = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();

			if (!registry.IsService(typeof(IAnalyticsEventStore)))
			{
				throw new InvalidOperationException("Register an IAnalyticsEventStore (for example InMemoryAnalyticsEventStore) before MapGeunedaAnalytics");
			}

			if (!registry.IsService(typeof(TimeProvider)))
			{
				throw new InvalidOperationException("Call AddGeunedaAnalytics before MapGeunedaAnalytics");
			}

			return endpoints.MapPost(prefix.TrimEnd('/') + "/events", AnalyticsEndpoint.HandleAsync);
		}
	}
}
