using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ClipDwg.Style;

/// <summary>
/// 설정 파일 읽기·쓰기.
/// <para>
/// AutoCAD 프로세스 안에서 도는 코드라 외부 JSON 라이브러리를 끌어오지 않는다.
/// (AutoCAD가 이미 자기 버전의 Newtonsoft.Json 등을 로드해 두어 충돌이 나기 쉽다.)
/// .NET Framework 기본 제공인 <see cref="DataContractJsonSerializer"/>만 쓴다.
/// </para>
/// </summary>
public static class SettingsStore
{
    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "clipdwg");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    /// <summary>설정을 읽는다. 파일이 없거나 깨졌으면 기본 설정을 돌려준다.</summary>
    public static SettingsFile Load() => Load(FilePath, out _);

    public static SettingsFile Load(string path, out string? error)
    {
        error = null;

        try
        {
            if (!File.Exists(path))
                return SettingsFile.CreateDefault();

            using FileStream stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(SettingsFile));
            if (serializer.ReadObject(stream) is SettingsFile loaded && loaded.Profiles.Count > 0)
                return loaded;

            error = "설정 파일에 프로파일이 없습니다. 기본값을 씁니다.";
        }
        catch (Exception ex)
        {
            error = $"설정 파일을 읽지 못해 기본값을 씁니다: {ex.Message}";
        }

        return SettingsFile.CreateDefault();
    }

    public static void Save(SettingsFile settings) => Save(settings, FilePath);

    public static void Save(SettingsFile settings, string path)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir!);

        string json;
        using (var buffer = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(SettingsFile));
            serializer.WriteObject(buffer, settings);
            json = Encoding.UTF8.GetString(buffer.ToArray());
        }

        // 손으로 열어 고칠 수 있게 들여쓴다. 직렬화기가 한 줄로만 내보내기 때문.
        File.WriteAllText(path, JsonFormatter.Indent(json), new UTF8Encoding(false));
    }
}
