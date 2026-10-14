using System;
using Geuneda.Core;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// 이벤트를 만든 앱의 정보입니다. 실행 중에는 바뀌지 않아 한 실행의 이벤트가 같은 인스턴스를 나눠 쓰고,
	/// 저장했다가 업데이트 뒤에 보낸 이벤트도 만들 때의 값을 그대로 씁니다.
	/// </summary>
	public sealed class AnalyticsAppInfo : IEquatable<AnalyticsAppInfo>
	{
		/// <param name="installId">설치 ID(core <see cref="IAppIdentity.InstallId"/>). 모르면 null</param>
		/// <param name="appVersion">앱 버전(<see cref="Application.version"/>)</param>
		/// <param name="platform">플랫폼 이름(<c>android</c>, <c>ios</c>, <c>editor</c> 등 소문자)</param>
		/// <param name="environment">빌드 환경 이름(<see cref="AppEnv.Name"/>)</param>
		/// <param name="deviceModel">기기 모델(<see cref="SystemInfo.deviceModel"/>)</param>
		/// <param name="operatingSystem">OS 와 버전(<see cref="SystemInfo.operatingSystem"/>)</param>
		public AnalyticsAppInfo(string installId, string appVersion, string platform, string environment, string deviceModel, string operatingSystem)
		{
			InstallId = installId;
			AppVersion = appVersion;
			Platform = platform;
			Environment = environment;
			DeviceModel = deviceModel;
			OperatingSystem = operatingSystem;
		}

		public string InstallId { get; }

		public string AppVersion { get; }

		public string Platform { get; }

		public string Environment { get; }

		public string DeviceModel { get; }

		public string OperatingSystem { get; }

		/// <summary>지금 실행 중인 앱의 정보를 읽습니다. 메인 스레드에서 부릅니다.</summary>
		/// <param name="identity">설치 ID 를 읽을 곳. null 이면 설치 ID 가 null 입니다</param>
		public static AnalyticsAppInfo Capture(IAppIdentity identity)
		{
			return new AnalyticsAppInfo(identity?.InstallId, Application.version, PlatformName(Application.platform), AppEnv.Name,
				SystemInfo.deviceModel, SystemInfo.operatingSystem);
		}

		/// <summary><see cref="RuntimePlatform"/> 을 짧은 소문자 이름으로 바꿉니다. 에디터는 OS 와 관계없이 <c>editor</c> 입니다.</summary>
		public static string PlatformName(RuntimePlatform platform)
		{
			switch (platform)
			{
				case RuntimePlatform.Android:
					return "android";
				case RuntimePlatform.IPhonePlayer:
					return "ios";
				case RuntimePlatform.OSXEditor:
				case RuntimePlatform.WindowsEditor:
				case RuntimePlatform.LinuxEditor:
					return "editor";
				case RuntimePlatform.OSXPlayer:
					return "macos";
				case RuntimePlatform.WindowsPlayer:
					return "windows";
				case RuntimePlatform.LinuxPlayer:
					return "linux";
				case RuntimePlatform.WebGLPlayer:
					return "webgl";
				default:
					return platform.ToString().ToLowerInvariant();
			}
		}

		public bool Equals(AnalyticsAppInfo other)
		{
			if (ReferenceEquals(this, other))
			{
				return true;
			}

			return other != null &&
			       InstallId == other.InstallId &&
			       AppVersion == other.AppVersion &&
			       Platform == other.Platform &&
			       Environment == other.Environment &&
			       DeviceModel == other.DeviceModel &&
			       OperatingSystem == other.OperatingSystem;
		}

		public override bool Equals(object obj)
		{
			return obj is AnalyticsAppInfo other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				var hash = InstallId?.GetHashCode() ?? 0;
				hash = (hash * 397) ^ (AppVersion?.GetHashCode() ?? 0);
				hash = (hash * 397) ^ (Platform?.GetHashCode() ?? 0);
				hash = (hash * 397) ^ (Environment?.GetHashCode() ?? 0);
				hash = (hash * 397) ^ (DeviceModel?.GetHashCode() ?? 0);
				return (hash * 397) ^ (OperatingSystem?.GetHashCode() ?? 0);
			}
		}
	}
}
