using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트 배치를 <see cref="AnalyticsWire"/> 형식의 UTF-8 JSON 으로 쓰고 읽습니다. 파라미터 값의 형식은 JSON 토큰 형식으로 남깁니다
	/// (정수 → <c>long</c>, 소수점·지수가 있는 수 → <c>double</c>). 날짜처럼 생긴 문자열을 바꾸지 않도록 날짜 해석을 끕니다.
	/// </summary>
	internal static class AnalyticsJson
	{
		// 짝 없는 서로게이트가 든 문자열도 저장·전송할 수 있도록 쓸 때는 U+FFFD 로 바꾸고, 읽을 때는 잘못된 바이트를 손상으로 봅니다
		private static readonly UTF8Encoding Utf8Writer = new UTF8Encoding(false, false);
		private static readonly UTF8Encoding Utf8Reader = new UTF8Encoding(false, true);

		public static byte[] SerializeBatch(IReadOnlyList<AnalyticsEvent> events)
		{
			using (var text = new StringWriter(CultureInfo.InvariantCulture))
			{
				using (var writer = new JsonTextWriter(text) { Formatting = Formatting.None })
				{
					writer.WriteStartObject();
					writer.WritePropertyName(AnalyticsWire.BatchVersion);
					writer.WriteValue(AnalyticsWire.Version);
					writer.WritePropertyName(AnalyticsWire.BatchEvents);
					writer.WriteStartArray();

					for (var i = 0; i < events.Count; i++)
					{
						WriteEvent(writer, events[i]);
					}

					writer.WriteEndArray();
					writer.WriteEndObject();
				}

				return Utf8Writer.GetBytes(text.ToString());
			}
		}

		/// <summary>배치를 읽습니다. 형식이 맞지 않으면 false 와 이유를 돌려줍니다(예외를 던지지 않음).</summary>
		public static bool TryDeserializeBatch(byte[] data, out List<AnalyticsEvent> events, out string error)
		{
			events = null;

			try
			{
				events = ReadBatch(Utf8Reader.GetString(data));
				error = null;
				return true;
			}
			catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is DecoderFallbackException ||
			                                  exception is ArgumentException || exception is OverflowException)
			{
				error = exception.Message;
				return false;
			}
		}

		private static void WriteEvent(JsonWriter writer, AnalyticsEvent analyticsEvent)
		{
			writer.WriteStartObject();
			WriteString(writer, AnalyticsWire.Id, analyticsEvent.Id);
			WriteString(writer, AnalyticsWire.Name, analyticsEvent.Name);
			writer.WritePropertyName(AnalyticsWire.Time);
			writer.WriteValue(new DateTimeOffset(analyticsEvent.TimestampUtc).ToUnixTimeMilliseconds());
			writer.WritePropertyName(AnalyticsWire.ServerTime);
			writer.WriteValue(analyticsEvent.IsServerTime);
			writer.WritePropertyName(AnalyticsWire.Sequence);
			writer.WriteValue(analyticsEvent.Sequence);
			WriteString(writer, AnalyticsWire.SessionId, analyticsEvent.SessionId);
			WriteString(writer, AnalyticsWire.UserId, analyticsEvent.UserId);

			var app = analyticsEvent.App;
			WriteString(writer, AnalyticsWire.InstallId, app.InstallId);
			WriteString(writer, AnalyticsWire.AppVersion, app.AppVersion);
			WriteString(writer, AnalyticsWire.Platform, app.Platform);
			WriteString(writer, AnalyticsWire.Environment, app.Environment);
			WriteString(writer, AnalyticsWire.Device, app.DeviceModel);
			WriteString(writer, AnalyticsWire.OperatingSystem, app.OperatingSystem);

			writer.WritePropertyName(AnalyticsWire.Parameters);
			writer.WriteStartObject();

			foreach (var parameter in analyticsEvent.Parameters)
			{
				writer.WritePropertyName(parameter.Name);
				WriteValue(writer, parameter.Value);
			}

			writer.WriteEndObject();

			if (analyticsEvent.UserProperties.Count > 0)
			{
				writer.WritePropertyName(AnalyticsWire.UserProperties);
				writer.WriteStartObject();

				foreach (var property in analyticsEvent.UserProperties)
				{
					writer.WritePropertyName(property.Key);
					writer.WriteValue(property.Value);
				}

				writer.WriteEndObject();
			}

			writer.WriteEndObject();
		}

		private static void WriteString(JsonWriter writer, string name, string value)
		{
			if (value == null)
			{
				return;
			}

			writer.WritePropertyName(name);
			writer.WriteValue(value);
		}

		private static void WriteValue(JsonWriter writer, AnalyticsValue value)
		{
			switch (value.Type)
			{
				case AnalyticsValueType.Integer:
					writer.WriteValue(value.IntegerValue);
					break;
				case AnalyticsValueType.Float:
					writer.WriteValue(value.FloatValue);
					break;
				case AnalyticsValueType.Boolean:
					writer.WriteValue(value.BooleanValue);
					break;
				default:
					writer.WriteValue(value.StringValue);
					break;
			}
		}

		private static List<AnalyticsEvent> ReadBatch(string json)
		{
			using (var reader = CreateReader(json))
			{
				Expect(reader, JsonToken.StartObject);
				int? version = null;
				List<AnalyticsEvent> events = null;

				while (Next(reader) != JsonToken.EndObject)
				{
					var property = (string) reader.Value;
					Next(reader);

					switch (property)
					{
						case AnalyticsWire.BatchVersion:
							version = checked((int) ReadInteger(reader, property));
							break;
						case AnalyticsWire.BatchEvents:
							events = ReadEvents(reader);
							break;
						default:
							reader.Skip();
							break;
					}
				}

				if (reader.Read())
				{
					throw new JsonReaderException("Unexpected content after the batch object");
				}

				if (version != AnalyticsWire.Version)
				{
					throw new FormatException($"Unsupported batch version {version?.ToString(CultureInfo.InvariantCulture) ?? "(missing)"}");
				}

				return events ?? throw new FormatException("Batch has no events");
			}
		}

		private static List<AnalyticsEvent> ReadEvents(JsonReader reader)
		{
			Require(reader, JsonToken.StartArray, AnalyticsWire.BatchEvents);
			var events = new List<AnalyticsEvent>();
			AnalyticsAppInfo lastApp = null;

			while (Next(reader) != JsonToken.EndArray)
			{
				events.Add(ReadEvent(reader, ref lastApp));
			}

			return events;
		}

		private static AnalyticsEvent ReadEvent(JsonReader reader, ref AnalyticsAppInfo lastApp)
		{
			Require(reader, JsonToken.StartObject, "event");
			string id = null, name = null, sessionId = null, userId = null;
			string installId = null, appVersion = null, platform = null, environment = null, device = null, os = null;
			long? time = null, sequence = null;
			bool? serverTime = null;
			List<AnalyticsParameter> parameters = null;
			Dictionary<string, string> userProperties = null;

			while (Next(reader) != JsonToken.EndObject)
			{
				var property = (string) reader.Value;
				Next(reader);

				switch (property)
				{
					case AnalyticsWire.Id:
						id = ReadString(reader, property);
						break;
					case AnalyticsWire.Name:
						name = ReadString(reader, property);
						break;
					case AnalyticsWire.Time:
						time = ReadInteger(reader, property);
						break;
					case AnalyticsWire.ServerTime:
						Require(reader, JsonToken.Boolean, property);
						serverTime = (bool) reader.Value;
						break;
					case AnalyticsWire.Sequence:
						sequence = ReadInteger(reader, property);
						break;
					case AnalyticsWire.SessionId:
						sessionId = ReadString(reader, property);
						break;
					case AnalyticsWire.UserId:
						userId = ReadString(reader, property);
						break;
					case AnalyticsWire.InstallId:
						installId = ReadString(reader, property);
						break;
					case AnalyticsWire.AppVersion:
						appVersion = ReadString(reader, property);
						break;
					case AnalyticsWire.Platform:
						platform = ReadString(reader, property);
						break;
					case AnalyticsWire.Environment:
						environment = ReadString(reader, property);
						break;
					case AnalyticsWire.Device:
						device = ReadString(reader, property);
						break;
					case AnalyticsWire.OperatingSystem:
						os = ReadString(reader, property);
						break;
					case AnalyticsWire.Parameters:
						parameters = ReadParameters(reader);
						break;
					case AnalyticsWire.UserProperties:
						userProperties = ReadUserProperties(reader);
						break;
					default:
						reader.Skip();
						break;
				}
			}

			if (id == null || name == null || time == null || serverTime == null || sequence == null || parameters == null)
			{
				throw new FormatException("Event is missing a required field");
			}

			var app = new AnalyticsAppInfo(installId, appVersion, platform, environment, device, os);

			// 한 배치의 이벤트는 보통 같은 앱 정보라 인스턴스를 나눠 씁니다
			if (app.Equals(lastApp))
			{
				app = lastApp;
			}

			lastApp = app;

			return new AnalyticsEvent(id, name, parameters, DateTimeOffset.FromUnixTimeMilliseconds(time.Value).UtcDateTime, serverTime.Value,
				sequence.Value, sessionId, userId, userProperties, app);
		}

		private static List<AnalyticsParameter> ReadParameters(JsonReader reader)
		{
			Require(reader, JsonToken.StartObject, AnalyticsWire.Parameters);
			var parameters = new List<AnalyticsParameter>();

			while (Next(reader) != JsonToken.EndObject)
			{
				var name = (string) reader.Value;
				Next(reader);

				switch (reader.TokenType)
				{
					case JsonToken.String:
						parameters.Add(new AnalyticsParameter(name, AnalyticsValue.String((string) reader.Value)));
						break;
					case JsonToken.Integer:
						parameters.Add(new AnalyticsParameter(name, AnalyticsValue.Integer(ToInt64(reader.Value, name))));
						break;
					case JsonToken.Float:
						parameters.Add(new AnalyticsParameter(name, AnalyticsValue.Float(Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture))));
						break;
					case JsonToken.Boolean:
						parameters.Add(new AnalyticsParameter(name, AnalyticsValue.Boolean((bool) reader.Value)));
						break;
					default:
						throw new FormatException($"Parameter '{name}' has unsupported token {reader.TokenType}");
				}
			}

			return parameters;
		}

		private static Dictionary<string, string> ReadUserProperties(JsonReader reader)
		{
			Require(reader, JsonToken.StartObject, AnalyticsWire.UserProperties);
			var properties = new Dictionary<string, string>(StringComparer.Ordinal);

			while (Next(reader) != JsonToken.EndObject)
			{
				var name = (string) reader.Value;
				Next(reader);
				properties[name] = ReadString(reader, name) ?? throw new FormatException($"User property '{name}' is null");
			}

			return properties;
		}

		private static JsonTextReader CreateReader(string json)
		{
			return new JsonTextReader(new StringReader(json))
			{
				DateParseHandling = DateParseHandling.None,
				FloatParseHandling = FloatParseHandling.Double,
				MaxDepth = 16
			};
		}

		private static JsonToken Next(JsonReader reader)
		{
			if (!reader.Read())
			{
				throw new JsonReaderException("Unexpected end of JSON");
			}

			return reader.TokenType;
		}

		private static void Expect(JsonReader reader, JsonToken token)
		{
			if (Next(reader) != token)
			{
				throw new FormatException($"Expected {token} but found {reader.TokenType}");
			}
		}

		private static void Require(JsonReader reader, JsonToken token, string property)
		{
			if (reader.TokenType != token)
			{
				throw new FormatException($"'{property}' must be {token} but was {reader.TokenType}");
			}
		}

		private static string ReadString(JsonReader reader, string property)
		{
			if (reader.TokenType == JsonToken.Null)
			{
				return null;
			}

			Require(reader, JsonToken.String, property);
			return (string) reader.Value;
		}

		private static long ReadInteger(JsonReader reader, string property)
		{
			Require(reader, JsonToken.Integer, property);
			return ToInt64(reader.Value, property);
		}

		private static long ToInt64(object value, string property)
		{
			// long 범위를 넘는 정수는 BigInteger 로 읽힙니다
			if (value is long number)
			{
				return number;
			}

			throw new FormatException($"'{property}' is out of the 64-bit integer range");
		}
	}
}
