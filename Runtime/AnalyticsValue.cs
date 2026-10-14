using System;
using System.Globalization;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트 파라미터 값 하나입니다. 문자열, 64비트 정수, 유한한 실수, 불리언만 담아 저장·재전송해도 형식이 그대로 남습니다.
	/// </summary>
	public readonly struct AnalyticsValue : IEquatable<AnalyticsValue>
	{
		private readonly string _string;
		private readonly long _integer;
		private readonly double _float;

		private AnalyticsValue(AnalyticsValueType type, string stringValue, long integer, double floatValue)
		{
			Type = type;
			_string = stringValue;
			_integer = integer;
			_float = floatValue;
		}

		public AnalyticsValueType Type { get; }

		/// <summary>문자열 값. 다른 형식이면 <see cref="InvalidOperationException"/> 입니다.</summary>
		public string StringValue => Type == AnalyticsValueType.String ? _string ?? string.Empty : throw WrongType(AnalyticsValueType.String);

		/// <summary>정수 값. 다른 형식이면 <see cref="InvalidOperationException"/> 입니다.</summary>
		public long IntegerValue => Type == AnalyticsValueType.Integer ? _integer : throw WrongType(AnalyticsValueType.Integer);

		/// <summary>실수 값. 다른 형식이면 <see cref="InvalidOperationException"/> 입니다.</summary>
		public double FloatValue => Type == AnalyticsValueType.Float ? _float : throw WrongType(AnalyticsValueType.Float);

		/// <summary>불리언 값. 다른 형식이면 <see cref="InvalidOperationException"/> 입니다.</summary>
		public bool BooleanValue => Type == AnalyticsValueType.Boolean ? _integer != 0 : throw WrongType(AnalyticsValueType.Boolean);

		/// <summary>문자열 값을 만듭니다. null 은 빈 문자열입니다.</summary>
		public static AnalyticsValue String(string value)
		{
			return new AnalyticsValue(AnalyticsValueType.String, value ?? string.Empty, 0, 0);
		}

		public static AnalyticsValue Integer(long value)
		{
			return new AnalyticsValue(AnalyticsValueType.Integer, null, value, 0);
		}

		/// <summary>실수 값을 만듭니다. NaN, 무한대는 JSON 으로 보낼 수 없어 <see cref="ArgumentException"/> 입니다.</summary>
		public static AnalyticsValue Float(double value)
		{
			if (double.IsNaN(value) || double.IsInfinity(value))
			{
				throw new ArgumentException($"Analytics value must be finite, but was {value}", nameof(value));
			}

			return new AnalyticsValue(AnalyticsValueType.Float, null, 0, value);
		}

		public static AnalyticsValue Boolean(bool value)
		{
			return new AnalyticsValue(AnalyticsValueType.Boolean, null, value ? 1 : 0, 0);
		}

		/// <summary>
		/// 파라미터 사전의 값을 바꿉니다. 문자열(null 은 빈 문자열), 불리언, 정수형(<c>ulong</c> 은 <c>long</c> 범위까지), <c>float</c>·<c>double</c>·<c>decimal</c>(실수로),
		/// <see cref="AnalyticsValue"/> 를 받습니다. 그 밖의 형식(enum 포함)은 <see cref="ArgumentException"/> 이니 호출부에서 문자열이나 숫자로 바꿔 넘깁니다.
		/// </summary>
		public static AnalyticsValue FromObject(object value)
		{
			switch (value)
			{
				case null:
					return String(null);
				case string text:
					return String(text);
				case bool flag:
					return Boolean(flag);
				case int number:
					return Integer(number);
				case long number:
					return Integer(number);
				case short number:
					return Integer(number);
				case byte number:
					return Integer(number);
				case sbyte number:
					return Integer(number);
				case ushort number:
					return Integer(number);
				case uint number:
					return Integer(number);
				case ulong number when number <= long.MaxValue:
					return Integer((long) number);
				case float number:
					return Float(number);
				case double number:
					return Float(number);
				case decimal number:
					return Float((double) number);
				case AnalyticsValue analyticsValue:
					return analyticsValue;
				case ulong number:
					throw new ArgumentException($"Analytics integer value {number} is larger than long.MaxValue", nameof(value));
				default:
					throw new ArgumentException(
						$"Analytics value type {value.GetType().FullName} is not supported. Use string, bool, an integer type, float, double or decimal", nameof(value));
			}
		}

		/// <summary>값을 상자에 담아 돌려줍니다(<c>string</c>, <c>long</c>, <c>double</c>, <c>bool</c>).</summary>
		public object ToObject()
		{
			switch (Type)
			{
				case AnalyticsValueType.Integer:
					return _integer;
				case AnalyticsValueType.Float:
					return _float;
				case AnalyticsValueType.Boolean:
					return _integer != 0;
				default:
					return _string ?? string.Empty;
			}
		}

		/// <summary>문화권과 무관한 문자열입니다. 실수는 왕복 가능한 형식, 불리언은 <c>true</c>/<c>false</c> 입니다.</summary>
		public override string ToString()
		{
			switch (Type)
			{
				case AnalyticsValueType.Integer:
					return _integer.ToString(CultureInfo.InvariantCulture);
				case AnalyticsValueType.Float:
					return _float.ToString("R", CultureInfo.InvariantCulture);
				case AnalyticsValueType.Boolean:
					return _integer != 0 ? "true" : "false";
				default:
					return _string ?? string.Empty;
			}
		}

		public bool Equals(AnalyticsValue other)
		{
			if (Type != other.Type)
			{
				return false;
			}

			switch (Type)
			{
				case AnalyticsValueType.String:
					return string.Equals(_string ?? string.Empty, other._string ?? string.Empty, StringComparison.Ordinal);
				case AnalyticsValueType.Float:
					return _float.Equals(other._float);
				default:
					return _integer == other._integer;
			}
		}

		public override bool Equals(object obj)
		{
			return obj is AnalyticsValue other && Equals(other);
		}

		public override int GetHashCode()
		{
			switch (Type)
			{
				case AnalyticsValueType.String:
					return StringComparer.Ordinal.GetHashCode(_string ?? string.Empty);
				case AnalyticsValueType.Float:
					return _float.GetHashCode();
				default:
					return ((int) Type * 397) ^ _integer.GetHashCode();
			}
		}

		public static bool operator ==(AnalyticsValue left, AnalyticsValue right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(AnalyticsValue left, AnalyticsValue right)
		{
			return !left.Equals(right);
		}

		private InvalidOperationException WrongType(AnalyticsValueType expected)
		{
			return new InvalidOperationException($"Analytics value is {Type}, not {expected}");
		}
	}
}
