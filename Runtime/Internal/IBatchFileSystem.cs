using System.IO;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics
{
	/// <summary>배치 저장소가 쓰는 파일 입출력입니다. 테스트가 특정 실패(목록, 읽기, 삭제)를 넣으려고 바꿉니다.</summary>
	internal interface IBatchFileSystem
	{
		string[] GetFiles(string directory, string pattern);

		byte[] ReadAllBytes(string path);

		void WriteAllBytes(string path, byte[] bytes);

		void Move(string source, string destination);

		void Delete(string path);

		bool FileExists(string path);

		void CreateDirectory(string path);
	}

	/// <summary><see cref="System.IO"/> 를 그대로 부르는 기본 구현입니다.</summary>
	internal sealed class SystemBatchFileSystem : IBatchFileSystem
	{
		public string[] GetFiles(string directory, string pattern)
		{
			return Directory.GetFiles(directory, pattern);
		}

		public byte[] ReadAllBytes(string path)
		{
			return File.ReadAllBytes(path);
		}

		public void WriteAllBytes(string path, byte[] bytes)
		{
			File.WriteAllBytes(path, bytes);
		}

		public void Move(string source, string destination)
		{
			File.Move(source, destination);
		}

		public void Delete(string path)
		{
			File.Delete(path);
		}

		public bool FileExists(string path)
		{
			return File.Exists(path);
		}

		public void CreateDirectory(string path)
		{
			Directory.CreateDirectory(path);
		}
	}
}
