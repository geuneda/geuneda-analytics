// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>이벤트 파라미터 하나(이름과 값)입니다.</summary>
	public readonly struct AnalyticsParameter
	{
		public AnalyticsParameter(string name, AnalyticsValue value)
		{
			Name = name;
			Value = value;
		}

		public string Name { get; }

		public AnalyticsValue Value { get; }

		public override string ToString()
		{
			return $"{Name}={Value}";
		}
	}
}
