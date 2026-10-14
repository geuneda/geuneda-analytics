using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geuneda.Analytics.Server
{
	/// <summary>
	/// <c>POST {prefix}/events</c> 처리기입니다. 415(JSON 아님), 413(본문·이벤트 수 초과), 400(배치 형식 오류), 200(이벤트별 결과)을 돌려줍니다.
	/// 저장소 예외는 잡지 않아 500 이 되고 클라이언트가 다시 보냅니다.
	/// </summary>
	internal static class AnalyticsEndpoint
	{
		public const string LoggerCategory = "Geuneda.Analytics.Server";

		public static async Task<IResult> HandleAsync(HttpContext context, [FromServices] IAnalyticsEventStore store,
			[FromServices] IOptions<AnalyticsServerOptions> options, [FromServices] TimeProvider time, [FromServices] ILoggerFactory loggers)
		{
			var settings = options.Value;
			var request = context.Request;
			var logger = loggers.CreateLogger(LoggerCategory);

			if (!request.HasJsonContentType())
			{
				return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
			}

			if (request.ContentLength > settings.MaxBodyBytes)
			{
				return Error(StatusCodes.Status413PayloadTooLarge, $"Body is larger than {settings.MaxBodyBytes} bytes");
			}

			var body = await ReadBodyAsync(request.Body, settings.MaxBodyBytes, context.RequestAborted);

			if (body == null)
			{
				return Error(StatusCodes.Status413PayloadTooLarge, $"Body is larger than {settings.MaxBodyBytes} bytes");
			}

			var outcome = AnalyticsBatchParser.Parse(body, settings, time.GetUtcNow());

			if (outcome.Error != null)
			{
				logger.LogWarning("Rejected an analytics batch: {Error}", outcome.Error);
				return Error(outcome.IsTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest, outcome.Error);
			}

			var appended = outcome.Events.Count > 0 ? await store.AppendAsync(outcome.Events, context.RequestAborted) : default;
			var rejected = new List<Dictionary<string, string>>(outcome.Rejected.Count);

			foreach (var rejection in outcome.Rejected)
			{
				rejected.Add(new Dictionary<string, string>
				{
					{ AnalyticsWire.ResponseRejectedId, rejection.Id },
					{ AnalyticsWire.ResponseRejectedReason, rejection.Reason }
				});
			}

			if (rejected.Count > 0)
			{
				logger.LogInformation("Rejected {Count} analytics events; first: {Reason}", rejected.Count, outcome.Rejected[0].Reason);
			}

			return Results.Json(new Dictionary<string, object>
			{
				{ AnalyticsWire.ResponseAccepted, appended.Accepted },
				{ AnalyticsWire.ResponseDuplicates, appended.Duplicates },
				{ AnalyticsWire.ResponseRejected, rejected }
			});
		}

		private static IResult Error(int status, string message)
		{
			return Results.Json(new Dictionary<string, string> { { "error", message } }, statusCode: status);
		}

		/// <summary>본문을 읽습니다. <paramref name="maxBytes"/> 를 넘으면 null 입니다(Content-Length 없이 보낸 본문도 막음).</summary>
		private static async Task<byte[]> ReadBodyAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
		{
			using (var buffer = new MemoryStream())
			{
				var chunk = new byte[16 * 1024];
				int read;

				while ((read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
				{
					if (buffer.Length + read > maxBytes)
					{
						return null;
					}

					buffer.Write(chunk, 0, read);
				}

				return buffer.ToArray();
			}
		}
	}
}
