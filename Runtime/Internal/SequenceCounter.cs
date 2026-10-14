using System.Globalization;
using Geuneda.Core;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>순번의 예약 상한을 저장하는 곳입니다. 테스트가 메모리 구현으로 바꿉니다.</summary>
	internal interface ISequenceStore
	{
		/// <summary>지난 실행까지 예약한 상한. 없으면 0 입니다.</summary>
		long Load();

		void Save(long reservedUpTo);
	}

	/// <summary>
	/// 설치마다 늘어나는 이벤트 순번입니다. 이벤트마다 저장하지 않도록 <see cref="BlockSize"/> 개씩 미리 예약해 저장하고,
	/// 다음 실행은 예약 상한 다음부터 셉니다. 비정상 종료하면 남은 예약 번호는 건너뛰지만 같은 번호가 두 번 나오지는 않습니다.
	/// </summary>
	internal sealed class SequenceCounter
	{
		public const int BlockSize = 100;

		private readonly ISequenceStore _store;
		private long _next;
		private long _reserved;

		public SequenceCounter(ISequenceStore store)
		{
			_store = store;
			_reserved = store.Load();
			_next = _reserved + 1;
		}

		public long Next()
		{
			if (_next > _reserved)
			{
				_reserved = _next + BlockSize - 1;
				_store.Save(_reserved);
			}

			return _next++;
		}
	}

	/// <summary>PlayerPrefs 에 예약 상한을 둡니다. 예약할 때마다 <see cref="PlayerPrefs.Save"/> 로 바로 씁니다.</summary>
	internal sealed class PlayerPrefsSequenceStore : ISequenceStore
	{
		public const string Key = "geuneda.analytics.sequence";

		private readonly ModuleLogger _logger;

		public PlayerPrefsSequenceStore(ModuleLogger logger)
		{
			_logger = logger;
		}

		public long Load()
		{
			var text = PlayerPrefs.GetString(Key, null);

			if (string.IsNullOrEmpty(text))
			{
				return 0;
			}

			if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
			{
				return value;
			}

			_logger.Warning($"Stored analytics sequence '{text}' is invalid; counting from 1 again");
			return 0;
		}

		public void Save(long reservedUpTo)
		{
			PlayerPrefs.SetString(Key, reservedUpTo.ToString(CultureInfo.InvariantCulture));
			PlayerPrefs.Save();
		}
	}
}
