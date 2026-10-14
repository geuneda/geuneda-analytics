using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>표준 이벤트 파라미터를 읽는 도우미입니다.</summary>
	internal static class AnalyticsStandardReader
	{
		public static bool TryString(AnalyticsEvent analyticsEvent, string name, out string value)
		{
			value = null;

			if (!analyticsEvent.TryGetParameter(name, out var parameter) || parameter.Type != AnalyticsValueType.String)
			{
				return false;
			}

			value = parameter.StringValue;
			return value.Length > 0;
		}

		/// <summary>실수나 정수를 실수로 읽습니다.</summary>
		public static bool TryNumber(AnalyticsEvent analyticsEvent, string name, out double value)
		{
			value = 0;

			if (!analyticsEvent.TryGetParameter(name, out var parameter))
			{
				return false;
			}

			switch (parameter.Type)
			{
				case AnalyticsValueType.Float:
					value = parameter.FloatValue;
					return true;
				case AnalyticsValueType.Integer:
					value = parameter.IntegerValue;
					return true;
				default:
					return false;
			}
		}

		public static bool TryInteger(AnalyticsEvent analyticsEvent, string name, out long value)
		{
			value = 0;

			if (!analyticsEvent.TryGetParameter(name, out var parameter) || parameter.Type != AnalyticsValueType.Integer)
			{
				return false;
			}

			value = parameter.IntegerValue;
			return true;
		}
	}

	/// <summary>ISO 4217 통화 코드 검사입니다.</summary>
	internal static class AnalyticsCurrency
	{
		public static bool IsValid(string currency)
		{
			if (currency == null || currency.Length != 3)
			{
				return false;
			}

			foreach (var c in currency)
			{
				if (c < 'A' || c > 'Z')
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>영문 3자를 대문자로 바꿉니다. 아니면 <see cref="ArgumentException"/> 입니다.</summary>
		public static string Normalize(string currency, string parameterName)
		{
			var upper = currency?.ToUpperInvariant();

			if (!IsValid(upper))
			{
				throw new ArgumentException($"Currency must be a 3-letter ISO 4217 code, but was '{currency}'", parameterName);
			}

			return upper;
		}
	}
}
