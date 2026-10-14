using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// <see cref="AnalyticsService"/> 가 플레이 중에 만드는 숨은 컴포넌트입니다. 앱 일시정지·복귀·종료를 넘기고,
	/// 1초마다 네트워크 연결 상태를 봐서 끊겼다가 다시 연결되면 알립니다.
	/// </summary>
	[AddComponentMenu("")]
	internal sealed class AnalyticsLifecycle : MonoBehaviour
	{
		private const float ReachabilityCheckInterval = 1f;

		private AnalyticsService _service;
		private float _sinceCheck;
		private bool _wasOffline;

		public void Bind(AnalyticsService service)
		{
			_service = service;
			_wasOffline = Application.internetReachability == NetworkReachability.NotReachable;
		}

		public void Unbind()
		{
			_service = null;
		}

		private void Update()
		{
			_sinceCheck += Time.unscaledDeltaTime;

			if (_sinceCheck < ReachabilityCheckInterval)
			{
				return;
			}

			_sinceCheck = 0f;
			var offline = Application.internetReachability == NetworkReachability.NotReachable;

			if (_wasOffline && !offline)
			{
				_service?.HandleNetworkRestored();
			}

			_wasOffline = offline;
		}

		private void OnApplicationPause(bool paused)
		{
			_service?.HandlePause(paused);
		}

		private void OnApplicationQuit()
		{
			_service?.HandleQuit();
		}
	}
}
