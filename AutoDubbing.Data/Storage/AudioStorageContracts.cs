namespace AutoDubbing.Data.Storage;

public interface IAudioStorage
{
    bool IsConfigured { get; }
    Task<string> UploadAsync(string objectPath, byte[] wav, CancellationToken token);
}
public interface IAudioCache
{
    Task<byte[]?> ReadAsync(string objectPath, CancellationToken token);
    Task WriteAsync(string objectPath, byte[] wav, CancellationToken token);
    void Remove(string objectPath);
}
public sealed class AudioStorageException(string code) : Exception(code)
{
    public string Code => Message;
}
