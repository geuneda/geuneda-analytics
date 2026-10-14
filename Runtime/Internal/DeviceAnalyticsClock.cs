using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>기본 시계입니다. 기기 시계를 쓰고 서버 시각이 아니라고 알립니다.</summary>
	internal sealed class DeviceAnalyticsClock : IAnalyticsClock
	{
		public DateTime UtcNow => DateTime.UtcNow;

		public bool IsServerTime => false;
	}
}
