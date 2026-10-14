using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Collector
{
	/// <summary>
	/// 자사 수집 서버(<c>Server~</c> 의 <c>MapGeunedaAnalytics</c>)로 이벤트를 배치(<see cref="AnalyticsWire"/> 형식 JSON)로 POST 합니다.
	/// 재전송된 이벤트는 서버가 이벤트 ID 로 걸러냅니다.
	/// </summary>
	/// <remarks>
	/// 결과: 2xx 는 받음(서버가 이벤트별로 거절한 것은 Warning), 연결 실패·타임아웃·취소와 408·429·5xx 는 재시도, 그 밖의 4xx 는 버림(Error).
	/// http 클라이언트의 미들웨어가 돌려준 그 밖의 실패 코드(예: 로그인 안 됨)는 재시도입니다.
	/// 본문이 <see cref="CollectorSinkOptions.MaxBodyBytes"/> 를 넘거나 서버가 413 을 주면 배치를 반으로 나눠 차례로 보내고, 이벤트 하나로도 넘으면 그 이벤트만 버립니다(Error).
	/// 나눈 뒤 한 조각이 재시도면 배치 전체를 재시도로 돌려줍니다(이미 받은 조각은 서버가 ID 로 거름).
	/// </remarks>
	public sealed class CollectorSink : IAnalyticsSink
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Analytics.Collector");

		private readonly IHttpClient _http;
		private readonly string _endpoint;
		private readonly TimeSpan? _timeout;
		private readonly int _maxBodyBytes;

		/// <param name="http">요청을 보낼 http 클라이언트. 인증이 필요한 서버면 account 의 <c>AuthTokenMiddleware</c> 를 넣은 클라이언트</param>
		/// <param name="options">설정. <see cref="CollectorSinkOptions.Endpoint"/> 는 필수</param>
		/// <exception cref="ArgumentNullException">인자가 null</exception>
		/// <exception cref="ArgumentException">주소가 비었거나 배치 설정이 범위를 벗어남</exception>
		public CollectorSink(IHttpClient http, CollectorSinkOptions options)
		{
			_http = http ?? throw new ArgumentNullException(nameof(http));

			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (string.IsNullOrWhiteSpace(options.Endpoint))
			{
				throw new ArgumentException("Collector endpoint is empty", nameof(options));
			}

			if (options.MaxBatchSize < 1 || options.MaxBatchSize > CollectorSinkOptions.MaxAllowedBatchSize)
			{
				throw new ArgumentException($"MaxBatchSize must be 1..{CollectorSinkOptions.MaxAllowedBatchSize}, but was {options.MaxBatchSize}", nameof(options));
			}

			if (options.BatchDelay < TimeSpan.Zero)
			{
				throw new ArgumentException("BatchDelay must not be negative", nameof(options));
			}

			if (options.Timeout.HasValue && options.Timeout.Value <= TimeSpan.Zero)
			{
				throw new ArgumentException("Timeout must be positive", nameof(options));
			}

			if (options.MaxBodyBytes < 1)
			{
				throw new ArgumentException("MaxBodyBytes must be positive", nameof(options));
			}

			// 잘못된 주소는 등록할 때 알 수 있게 미리 만들어 봅니다(오용이면 예외)
			HttpRequest.Post(options.Endpoint);

			_endpoint = options.Endpoint;
			_timeout = options.Timeout;
			_maxBodyBytes = options.MaxBodyBytes;
			MaxBatchSize = options.MaxBatchSize;
			BatchDelay = options.BatchDelay;
		}

		public AnalyticsSinks Id => AnalyticsSinks.Collector;

		public int MaxBatchSize { get; }

		public TimeSpan BatchDelay { get; }

		public bool IsReady => true;

		public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
		{
			return SendPartAsync(events, 0, events.Count, cancellationToken);
		}

		private async UniTask<AnalyticsSendResult> SendPartAsync(IReadOnlyList<AnalyticsEvent> events, int start, int count, CancellationToken cancellationToken)
		{
			var part = Slice(events, start, count);
			var body = AnalyticsJson.SerializeBatch(part);

			if (body.Length > _maxBodyBytes)
			{
				return await SplitAsync(events, start, count, $"{body.Length} bytes is larger than MaxBodyBytes {_maxBodyBytes}", cancellationToken);
			}

			var request = HttpRequest.Post(_endpoint).WithBody(body, AnalyticsWire.ContentType);

			if (_timeout.HasValue)
			{
				request = request.WithTimeout(_timeout.Value);
			}

			var result = await _http.SendAsync(request, cancellationToken);

			if (!result.TryGetValue(out var response))
			{
				if (result.Code != ResultCodes.Cancelled)
				{
					Logger.Warning($"Could not send {count} analytics events: {result}");
				}

				return AnalyticsSendResult.Retry;
			}

			var status = response.StatusCode;

			if (response.IsSuccessStatusCode)
			{
				LogRejectedEvents(response);
				return AnalyticsSendResult.Delivered;
			}

			if (status == 413)
			{
				return await SplitAsync(events, start, count, "the collector returned HTTP 413", cancellationToken);
			}

			if (status == 408 || status == 429 || status >= 500)
			{
				Logger.Warning($"Analytics collector returned HTTP {status} for {count} events; will retry");
				return AnalyticsSendResult.Retry;
			}

			Logger.Error($"Analytics collector rejected {count} events with HTTP {status}: {Truncate(response.ReadText())}");
			return AnalyticsSendResult.Rejected;
		}

		/// <summary>너무 큰 배치를 반으로 나눠 차례로 보냅니다. 이벤트 하나면 버립니다. 한 조각이라도 재시도면 재시도, 하나라도 받았으면 받음입니다.</summary>
		private async UniTask<AnalyticsSendResult> SplitAsync(IReadOnlyList<AnalyticsEvent> events, int start, int count, string reason,
			CancellationToken cancellationToken)
		{
			if (count == 1)
			{
				Logger.Error($"Dropping analytics event {events[start].Name} ({events[start].Id}): {reason}");
				return AnalyticsSendResult.Rejected;
			}

			var half = count / 2;
			var first = await SendPartAsync(events, start, half, cancellationToken);

			if (first == AnalyticsSendResult.Retry)
			{
				return AnalyticsSendResult.Retry;
			}

			var second = await SendPartAsync(events, start + half, count - half, cancellationToken);

			if (second == AnalyticsSendResult.Retry)
			{
				return AnalyticsSendResult.Retry;
			}

			return first == AnalyticsSendResult.Delivered || second == AnalyticsSendResult.Delivered ? AnalyticsSendResult.Delivered : AnalyticsSendResult.Rejected;
		}

		private static IReadOnlyList<AnalyticsEvent> Slice(IReadOnlyList<AnalyticsEvent> events, int start, int count)
		{
			if (start == 0 && count == events.Count)
			{
				return events;
			}

			var part = new AnalyticsEvent[count];

			for (var i = 0; i < count; i++)
			{
				part[i] = events[start + i];
			}

			return part;
		}

		public void SetUserId(string userId)
		{
			// 사용자 ID 는 이벤트마다 실어 보냅니다
		}

		public void SetUserProperty(string name, string value)
		{
			// 사용자 속성은 이벤트마다 실어 보냅니다
		}

		private static void LogRejectedEvents(HttpResponse response)
		{
			if (response.Body == null || response.Body.Length == 0)
			{
				return;
			}

			JArray rejected;

			try
			{
				rejected = JObject.Parse(response.ReadText())[AnalyticsWire.ResponseRejected] as JArray;
			}
			catch (JsonException exception)
			{
				Logger.Warning($"Analytics collector response is not valid JSON: {exception.Message}");
				return;
			}

			if (rejected == null || rejected.Count == 0)
			{
				return;
			}

			var builder = new StringBuilder();
			builder.Append("Analytics collector rejected ").Append(rejected.Count).Append(" events:");

			for (var i = 0; i < rejected.Count && i < 5; i++)
			{
				var entry = rejected[i] as JObject;
				builder.Append(' ').Append(entry?[AnalyticsWire.ResponseRejectedId]).Append(" (").Append(entry?[AnalyticsWire.ResponseRejectedReason]).Append(')');
			}

			Logger.Warning(builder.ToString());
		}

		private static string Truncate(string text)
		{
			return text.Length <= 300 ? text : text.Substring(0, 300) + "...";
		}
	}
}
