using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>
	/// sink 하나의 보내지 못한 배치를 파일로 둡니다. 파일 하나가 배치 하나이고, 이름 순서가 저장 순서입니다.
	/// 메인 스레드에서만 쓰고 잠금이 없습니다. 쓰기는 임시 파일에 쓴 뒤 이름을 바꿔 반쯤 쓴 파일이 남지 않게 합니다.
	/// </summary>
	/// <remarks>
	/// 폴더 목록은 <see cref="TryLoad"/> 가 성공한 뒤에만 씁니다. 그 전에는 이번 실행에서 쓴 파일만 목록에 있고, 쓰기는 목록 없이도 합니다
	/// (목록을 읽지 못해도 새 배치는 저장할 수 있게).
	/// </remarks>
	internal sealed class EventFileStore
	{
		private const string Extension = ".json";
		private const string TempExtension = ".tmp";

		private readonly ModuleLogger _logger;
		private readonly IBatchFileSystem _fileSystem;
		private readonly string _directory;
		private readonly int _maxFiles;
		private readonly List<string> _files = new List<string>();
		private bool _loaded;
		private int _counter;

		public EventFileStore(string directory, int maxFiles, ModuleLogger logger, IBatchFileSystem fileSystem)
		{
			_directory = directory;
			_maxFiles = maxFiles;
			_logger = logger;
			_fileSystem = fileSystem;
		}

		/// <summary>목록에 있는 배치 수입니다(목록을 아직 못 읽었으면 이번 실행에 쓴 것만).</summary>
		public int Count => _files.Count;

		/// <summary>
		/// 폴더의 배치 목록을 읽습니다(성공할 때까지). 폴더가 없으면 빈 목록이고, 그 밖의 이유로 목록을 못 읽으면 Warning 후 false 입니다(다음 호출에서 다시 읽음).
		/// 지난 실행이 남긴 임시 파일은 하나씩 지우고, 지우지 못해도 목록은 그대로 씁니다. 목록을 합친 뒤 파일 수 상한을 적용합니다
		/// (목록을 못 읽는 동안 이번 실행에서 쓴 파일과 이전 실행의 파일이 합쳐질 수 있음).
		/// </summary>
		public bool TryLoad()
		{
			if (_loaded)
			{
				return true;
			}

			string[] stored;

			try
			{
				stored = _fileSystem.GetFiles(_directory, "*" + Extension);
			}
			catch (DirectoryNotFoundException)
			{
				_loaded = true;
				return true;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				_logger.Warning($"Could not list stored analytics batches in {_directory}: {exception.Message}");
				return false;
			}

			DeleteTempFiles();

			// 목록을 읽기 전에 이번 실행에서 쓴 파일도 디스크 목록에 들어 있습니다
			foreach (var path in stored)
			{
				var name = Path.GetFileName(path);

				if (!_files.Contains(name))
				{
					_files.Add(name);
				}
			}

			_files.Sort(StringComparer.Ordinal);
			_loaded = true;
			TrimOverflow(null);
			return true;
		}

		/// <summary>목록에서 가장 오래된 배치 파일 이름입니다.</summary>
		public bool TryPeekOldest(out string file)
		{
			file = _files.Count > 0 ? _files[0] : null;
			return file != null;
		}

		/// <summary>
		/// 배치를 새 파일로 씁니다. 파일 수가 상한이면 가장 오래된 파일부터 지웁니다(<paramref name="keep"/> 는 빼고).
		/// 디스크 오류면 Warning 을 남기고 false 입니다.
		/// </summary>
		public bool TryWrite(IReadOnlyList<AnalyticsEvent> events, string keep, out string file)
		{
			file = null;

			try
			{
				_fileSystem.CreateDirectory(_directory);
				var name = NextName();
				WriteFile(name, events);
				file = name;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				_logger.Warning($"Could not store {events.Count} analytics events in {_directory}: {exception.Message}");
				return false;
			}

			_files.Add(file);
			TrimOverflow(keep);
			return true;
		}

		/// <summary>
		/// 배치 파일 하나를 <paramref name="maxBatchSize"/> 이하의 파일 여러 개로 나눕니다. 나눈 파일은 원본 자리(순서)에 두고, 모두 쓴 뒤에 원본을 지우고
		/// 파일 수 상한을 적용합니다. 디스크 오류면 쓴 조각을 지우고 원본을 둔 채 Warning 후 false 입니다.
		/// 원본을 지우기 전에 앱이 죽으면 다음 실행에서 같은 이벤트를 한 번 더 보낼 수 있습니다.
		/// </summary>
		public bool TrySplit(string file, IReadOnlyList<AnalyticsEvent> events, int maxBatchSize)
		{
			var baseName = file.Substring(0, file.Length - Extension.Length);
			var written = new List<string>();

			try
			{
				for (var start = 0; start < events.Count; start += maxBatchSize)
				{
					var count = Math.Min(maxBatchSize, events.Count - start);
					var part = new AnalyticsEvent[count];

					for (var i = 0; i < count; i++)
					{
						part[i] = events[start + i];
					}

					var name = SplitName(baseName, written.Count);
					WriteFile(name, part);
					written.Add(name);
				}
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				_logger.Warning($"Could not split stored analytics batch {file}: {exception.Message}");

				foreach (var name in written)
				{
					TryDeleteFile(name);
				}

				return false;
			}

			var index = _files.IndexOf(file);
			_files.InsertRange(index < 0 ? 0 : index, written);
			Delete(file);
			TrimOverflow(null);
			return true;
		}

		/// <summary>배치를 읽습니다. 손상됐거나 사라진 파일은 목록에서 빼고 <see cref="ReadStatus.Discarded"/>, 디스크 오류는 파일을 두고 <see cref="ReadStatus.Unavailable"/> 입니다.</summary>
		public ReadStatus TryRead(string file, out List<AnalyticsEvent> events)
		{
			events = null;
			byte[] data;

			try
			{
				data = _fileSystem.ReadAllBytes(Path.Combine(_directory, file));
			}
			catch (Exception exception) when (exception is FileNotFoundException || exception is DirectoryNotFoundException)
			{
				_files.Remove(file);
				return ReadStatus.Discarded;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				_logger.Warning($"Could not read stored analytics batch {file}: {exception.Message}");
				return ReadStatus.Unavailable;
			}

			if (AnalyticsJson.TryDeserializeBatch(data, out events, out var error))
			{
				return ReadStatus.Ok;
			}

			_logger.Warning($"Deleting corrupt analytics batch {file}: {error}");
			Delete(file);
			return ReadStatus.Discarded;
		}

		/// <summary>배치 파일을 지웁니다. 목록에 없으면 아무것도 하지 않습니다. 디스크 오류는 Warning 입니다(다음 실행에서 다시 보낼 수 있음).</summary>
		public void Delete(string file)
		{
			if (!_files.Remove(file))
			{
				return;
			}

			TryDeleteFile(file);
		}

		private void TryDeleteFile(string file)
		{
			try
			{
				_fileSystem.Delete(Path.Combine(_directory, file));
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				_logger.Warning($"Could not delete analytics batch {file}: {exception.Message}");
			}
		}

		/// <summary>임시 파일에 쓴 뒤 이름을 바꿔 반쯤 쓴 배치 파일이 남지 않게 합니다.</summary>
		private void WriteFile(string name, IReadOnlyList<AnalyticsEvent> events)
		{
			var temp = Path.Combine(_directory, name + TempExtension);
			_fileSystem.WriteAllBytes(temp, AnalyticsJson.SerializeBatch(events));
			_fileSystem.Move(temp, Path.Combine(_directory, name));
		}

		private void DeleteTempFiles()
		{
			string[] temps;

			try
			{
				temps = _fileSystem.GetFiles(_directory, "*" + TempExtension);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				_logger.Warning($"Could not list temporary analytics files in {_directory}: {exception.Message}");
				return;
			}

			foreach (var temp in temps)
			{
				try
				{
					_fileSystem.Delete(temp);
				}
				catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
				{
					_logger.Warning($"Could not delete temporary analytics file {temp}: {exception.Message}");
				}
			}
		}

		/// <summary>원본 이름 뒤에 조각 번호를 붙입니다. 이름 순서상 원본 자리 바로 앞에 옵니다. 같은 이름이 있으면(지난 실행이 나누다 멈춤) 번호를 더 붙입니다.</summary>
		private string SplitName(string baseName, int part)
		{
			var stem = baseName + "-" + part.ToString("D3", CultureInfo.InvariantCulture);

			for (var attempt = 0;; attempt++)
			{
				var name = (attempt == 0 ? stem : stem + "-" + attempt.ToString(CultureInfo.InvariantCulture)) + Extension;

				if (!_files.Contains(name) && !_fileSystem.FileExists(Path.Combine(_directory, name)))
				{
					return name;
				}
			}
		}

		private string NextName()
		{
			// 시각(UTC 틱) + 실행 안 카운터. 이름 순서가 저장 순서이고, 같은 틱이나 시계가 되돌아간 경우는 카운터와 존재 확인으로 겹치지 않게 합니다
			while (true)
			{
				var name = DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture) + "-" +
				           (_counter++ % 1000000).ToString("D6", CultureInfo.InvariantCulture) + Extension;

				if (!_files.Contains(name) && !_fileSystem.FileExists(Path.Combine(_directory, name)))
				{
					return name;
				}
			}
		}

		private void TrimOverflow(string keep)
		{
			var dropped = 0;

			for (var i = 0; _files.Count > _maxFiles && i < _files.Count;)
			{
				if (_files[i] == keep)
				{
					i++;
					continue;
				}

				Delete(_files[i]);
				dropped++;
			}

			if (dropped > 0)
			{
				_logger.Warning($"Dropped {dropped} oldest stored analytics batches in {_directory} (limit {_maxFiles})");
			}
		}

		public enum ReadStatus
		{
			Ok,
			Discarded,
			Unavailable
		}
	}
}
