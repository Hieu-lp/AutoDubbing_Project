using System.Text.RegularExpressions;

namespace AutoDubbing.Data.Storage;

// Cache đã hoàn thành TTS để retry upload/SQL không tốn lượt TTS khác.
public sealed class LocalAudioCache(string root) : IAudioCache
{
    private string Resolve(string path)
    {
        if (!Regex.IsMatch(path, @"\Agenerated/poi-[1-9]\d*/[a-z-]+/[a-f0-9]{64}\.wav\z"))
            throw new AudioStorageException("InvalidAudioCachePath");
        return Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
    }
    public async Task<byte[]?> ReadAsync(string objectPath, CancellationToken token)
    {
        var path = Resolve(objectPath);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 6 * 1024 * 1024) throw new AudioStorageException("AudioTooLarge");
        return await File.ReadAllBytesAsync(path, token);
    }
    public async Task WriteAsync(string objectPath, byte[] wav, CancellationToken token)
    {
        var path = Resolve(objectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, wav, token);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Remove(string objectPath)
    {
        var path = Resolve(objectPath);
        if (File.Exists(path)) File.Delete(path);
    }
}
