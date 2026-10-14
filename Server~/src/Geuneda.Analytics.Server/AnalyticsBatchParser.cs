using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Geuneda.Analytics.Server
{
	/// <summary>
	/// 요청 본문(<see cref="AnalyticsWire"/> 형식 배치)을 읽습니다. 배치 자체가 잘못되면 <see cref="Outcome.Error"/>, 이벤트 하나가 잘못되면 그 이벤트만 거절합니다.
	/// </summary>
	internal static class AnalyticsBatchParser
	{
		// DateTimeOffset 이 나타낼 수 있는 유닉스 ms 범위
		private const long MinUnixMilliseconds = -62135596800000;
		private const long MaxUnixMilliseconds = 253402300799999;

		public static Outcome Parse(ReadOnlyMemory<byte> body, AnalyticsServerOptions options, DateTimeOffset receivedAt)
		{
			JsonDocument document;

			try
			{
				document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
			}
			catch (JsonException exception)
			{
				return Outcome.Invalid($"Body is not valid JSON: {exception.Message}");
			}

			using (document)
			{
				var root = document.RootElement;

				if (root.ValueKind != JsonValueKind.Object)
				{
					return Outcome.Invalid("Body must be a JSON object");
				}

				if (!root.TryGetProperty(AnalyticsWire.BatchVersion, out var version) || version.ValueKind != JsonValueKind.Number ||
				    !version.TryGetInt32(out var versionNumber) || versionNumber != AnalyticsWire.Version)
				{
					return Outcome.Invalid($"Unsupported batch version (expected {AnalyticsWire.Version})");
				}

				if (!root.TryGetProperty(AnalyticsWire.BatchEvents, out var events) || events.ValueKind != JsonValueKind.Array)
				{
					return Outcome.Invalid($"'{AnalyticsWire.BatchEvents}' must be an array");
				}

				if (events.GetArrayLength() > options.MaxEventsPerBatch)
				{
					return Outcome.TooLarge($"A batch may hold at most {options.MaxEventsPerBatch} events");
				}

				var outcome = new Outcome();

				foreach (var element in events.EnumerateArray())
				{
					string reason;
					CollectedEvent collected;

					try
					{
						reason = TryRead(element, options, receivedAt, out collected);
					}
					catch (InvalidOperationException)
					{
						// 짝이 맞지 않는 서로게이트 이스케이프(\uD800 등)는 문자열로 읽을 때 InvalidOperationException 입니다
						reason = "event contains an invalid string";
						collected = null;
					}

					if (reason == null)
					{
						outcome.Events.Add(collected);
					}
					else
					{
						outcome.Rejected.Add(new Rejection(RejectedId(element, options), reason));
					}
				}

				return outcome;
			}
		}

		private static string TryRead(JsonElement element, AnalyticsServerOptions options, DateTimeOffset receivedAt, out CollectedEvent collected)
		{
			collected = null;

			if (element.ValueKind != JsonValueKind.Object)
			{
				return "event must be an object";
			}

			string id = null, name = null, sessionId = null, userId = null;
			string installId = null, appVersion = null, platform = null, environment = null, device = null, os = null;
			long? time = null, sequence = null;
			bool? serverTime = null;
			Dictionary<string, object> parameters = null;
			Dictionary<string, string> userProperties = null;
			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (var property in element.EnumerateObject())
			{
				if (!seen.Add(property.Name))
				{
					return $"duplicate field '{property.Name}'";
				}

				string reason = null;
				var value = property.Value;

				switch (property.Name)
				{
					case AnalyticsWire.Id:
						reason = ReadRequiredString(value, property.Name, options.MaxNameLength, out id);
						break;
					case AnalyticsWire.Name:
						reason = ReadRequiredString(value, property.Name, options.MaxNameLength, out name);
						break;
					case AnalyticsWire.Time:
						reason = ReadInteger(value, property.Name, out time);

						if (reason == null && (time < MinUnixMilliseconds || time > MaxUnixMilliseconds))
						{
							reason = $"'{property.Name}' is out of range";
						}

						break;
					case AnalyticsWire.ServerTime:
						if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
						{
							serverTime = value.GetBoolean();
						}
						else
						{
							reason = $"'{property.Name}' must be a boolean";
						}

						break;
					case AnalyticsWire.Sequence:
						reason = ReadInteger(value, property.Name, out sequence);
						break;
					case AnalyticsWire.SessionId:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out sessionId);
						break;
					case AnalyticsWire.UserId:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out userId);
						break;
					case AnalyticsWire.InstallId:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out installId);
						break;
					case AnalyticsWire.AppVersion:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out appVersion);
						break;
					case AnalyticsWire.Platform:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out platform);
						break;
					case AnalyticsWire.Environment:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out environment);
						break;
					case AnalyticsWire.Device:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out device);
						break;
					case AnalyticsWire.OperatingSystem:
						reason = ReadOptionalString(value, property.Name, options.MaxStringLength, out os);
						break;
					case AnalyticsWire.Parameters:
						reason = ReadParameters(value, options, out parameters);
						break;
					case AnalyticsWire.UserProperties:
						reason = ReadUserProperties(value, options, out userProperties);
						break;
				}

				if (reason != null)
				{
					return reason;
				}
			}

			var missing = id == null ? AnalyticsWire.Id :
				name == null ? AnalyticsWire.Name :
				time == null ? AnalyticsWire.Time :
				serverTime == null ? AnalyticsWire.ServerTime :
				sequence == null ? AnalyticsWire.Sequence :
				parameters == null ? AnalyticsWire.Parameters : null;

			if (missing != null)
			{
				return $"missing '{missing}'";
			}

			collected = new CollectedEvent(id, name, DateTimeOffset.FromUnixTimeMilliseconds(time.Value), serverTime.Value, sequence.Value, sessionId, userId,
				installId, appVersion, platform, environment, device, os, parameters,
				userProperties ?? new Dictionary<string, string>(StringComparer.Ordinal), receivedAt);
			return null;
		}

		private static string ReadParameters(JsonElement value, AnalyticsServerOptions options, out Dictionary<string, object> parameters)
		{
			parameters = null;

			if (value.ValueKind != JsonValueKind.Object)
			{
				return $"'{AnalyticsWire.Parameters}' must be an object";
			}

			var read = new Dictionary<string, object>(StringComparer.Ordinal);

			foreach (var parameter in value.EnumerateObject())
			{
				if (read.Count >= options.MaxParameters)
				{
					return $"more than {options.MaxParameters} parameters";
				}

				var reason = CheckName(parameter.Name, "parameter", options);

				if (reason != null)
				{
					return reason;
				}

				if (read.ContainsKey(parameter.Name))
				{
					return $"duplicate parameter '{parameter.Name}'";
				}

				switch (parameter.Value.ValueKind)
				{
					case JsonValueKind.String:
						var text = parameter.Value.GetString();

						if (text.Length > options.MaxStringLength)
						{
							return $"parameter '{parameter.Name}' is longer than {options.MaxStringLength}";
						}

						read.Add(parameter.Name, text);
						break;
					case JsonValueKind.Number:
						// 소수점·지수가 없는 수는 정수(long), 있는 수는 실수(double)입니다(클라이언트 AnalyticsJson 과 같은 규칙)
						if (IsIntegerToken(parameter.Value))
						{
							if (!parameter.Value.TryGetInt64(out var integer))
							{
								return $"parameter '{parameter.Name}' is out of the 64-bit integer range";
							}

							read.Add(parameter.Name, integer);
						}
						else if (parameter.Value.TryGetDouble(out var number) && double.IsFinite(number))
						{
							read.Add(parameter.Name, number);
						}
						else
						{
							return $"parameter '{parameter.Name}' is not a finite number";
						}

						break;
					case JsonValueKind.True:
					case JsonValueKind.False:
						read.Add(parameter.Name, parameter.Value.GetBoolean());
						break;
					default:
						return $"parameter '{parameter.Name}' must be a string, number or boolean";
				}
			}

			parameters = read;
			return null;
		}

		private static bool IsIntegerToken(JsonElement number)
		{
			var raw = number.GetRawText();
			return raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
		}

		private static string ReadUserProperties(JsonElement value, AnalyticsServerOptions options, out Dictionary<string, string> properties)
		{
			properties = null;

			if (value.ValueKind != JsonValueKind.Object)
			{
				return $"'{AnalyticsWire.UserProperties}' must be an object";
			}

			var read = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach (var property in value.EnumerateObject())
			{
				if (read.Count >= options.MaxParameters)
				{
					return $"more than {options.MaxParameters} user properties";
				}

				var reason = CheckName(property.Name, "user property", options);

				if (reason != null)
				{
					return reason;
				}

				if (read.ContainsKey(property.Name))
				{
					return $"duplicate user property '{property.Name}'";
				}

				if (property.Value.ValueKind != JsonValueKind.String)
				{
					return $"user property '{property.Name}' must be a string";
				}

				var text = property.Value.GetString();

				if (text.Length > options.MaxStringLength)
				{
					return $"user property '{property.Name}' is longer than {options.MaxStringLength}";
				}

				read.Add(property.Name, text);
			}

			properties = read;
			return null;
		}

		private static string CheckName(string name, string kind, AnalyticsServerOptions options)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return $"{kind} name is empty";
			}

			return name.Length > options.MaxNameLength ? $"{kind} name is longer than {options.MaxNameLength}" : null;
		}

		private static string ReadRequiredString(JsonElement value, string field, int maxLength, out string text)
		{
			text = null;

			if (value.ValueKind != JsonValueKind.String)
			{
				return $"'{field}' must be a string";
			}

			var read = value.GetString();

			if (string.IsNullOrWhiteSpace(read))
			{
				return $"'{field}' is empty";
			}

			if (read.Length > maxLength)
			{
				return $"'{field}' is longer than {maxLength}";
			}

			text = read;
			return null;
		}

		private static string ReadOptionalString(JsonElement value, string field, int maxLength, out string text)
		{
			text = null;

			if (value.ValueKind == JsonValueKind.Null)
			{
				return null;
			}

			if (value.ValueKind != JsonValueKind.String)
			{
				return $"'{field}' must be a string";
			}

			var read = value.GetString();

			if (read.Length > maxLength)
			{
				return $"'{field}' is longer than {maxLength}";
			}

			text = read;
			return null;
		}

		private static string ReadInteger(JsonElement value, string field, out long? integer)
		{
			integer = null;

			if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var read))
			{
				return $"'{field}' must be a 64-bit integer";
			}

			integer = read;
			return null;
		}

		private static string RejectedId(JsonElement element, AnalyticsServerOptions options)
		{
			if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(AnalyticsWire.Id, out var id) || id.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			string text;

			try
			{
				text = id.GetString();
			}
			catch (InvalidOperationException)
			{
				return null;
			}

			return text.Length > options.MaxNameLength ? text.Substring(0, options.MaxNameLength) : text;
		}

		public sealed class Outcome
		{
			public List<CollectedEvent> Events { get; } = new List<CollectedEvent>();

			public List<Rejection> Rejected { get; } = new List<Rejection>();

			/// <summary>배치 전체를 거절한 이유. null 이면 배치는 읽었습니다.</summary>
			public string Error { get; private set; }

			/// <summary><see cref="Error"/> 가 크기 초과(413)인지</summary>
			public bool IsTooLarge { get; private set; }

			public static Outcome Invalid(string error)
			{
				return new Outcome { Error = error };
			}

			public static Outcome TooLarge(string error)
			{
				return new Outcome { Error = error, IsTooLarge = true };
			}
		}

		public readonly struct Rejection
		{
			public Rejection(string id, string reason)
			{
				Id = id;
				Reason = reason;
			}

			public string Id { get; }

			public string Reason { get; }
		}
	}
}
