using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트를 콘솔(core 로거, 모듈 <c>Geuneda.Analytics.Console</c>, Info)에 <c>이름 {키=값, ...}</c> 로 씁니다. 개발 확인용이라 운영 빌드에는 등록하지 않습니다.
	/// </summary>
	public sealed class ConsoleSink : IAnalyticsSink
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Analytics.Console");

		private readonly StringBuilder _builder = new StringBuilder();

		public AnalyticsSinks Id => AnalyticsSinks.Console;

		public int MaxBatchSize => 100;

		public TimeSpan BatchDelay => TimeSpan.Zero;

		public bool IsReady => true;

		public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
		{
			foreach (var analyticsEvent in events)
			{
				Logger.Info(Format(analyticsEvent));
			}

			return UniTask.FromResult(AnalyticsSendResult.Delivered);
		}

		public void SetUserId(string userId)
		{
			Logger.Info($"user_id = {userId ?? "(none)"}");
		}

		public void SetUserProperty(string name, string value)
		{
			Logger.Info($"user_property {name} = {value ?? "(removed)"}");
		}

		private string Format(AnalyticsEvent analyticsEvent)
		{
			_builder.Clear();
			_builder.Append(analyticsEvent.Name).Append(" {");

			for (var i = 0; i < analyticsEvent.Parameters.Count; i++)
			{
				if (i > 0)
				{
					_builder.Append(", ");
				}

				var parameter = analyticsEvent.Parameters[i];
				_builder.Append(parameter.Name).Append('=').Append(parameter.Value.ToString());
			}

			return _builder.Append('}').ToString();
		}
	}
}
