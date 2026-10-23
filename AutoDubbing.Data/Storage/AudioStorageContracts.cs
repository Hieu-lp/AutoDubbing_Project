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
public sealed record StorageExceptionInfo(string Type, int HResult, string? HttpRequestError = null,
    string? SocketError = null, int? NativeErrorCode = null);
public sealed record AudioStorageFailureDetails(string Stage, double ElapsedSeconds, long BytesWritten,
    long TotalBytes, int? HttpStatus = null, string? StorageCode = null,
    IReadOnlyList<StorageExceptionInfo>? ExceptionChain = null);
public sealed class AudioStorageException(string code, AudioStorageFailureDetails? details = null) : Exception(code)
{
    public string Code => Message;
    public AudioStorageFailureDetails? Details { get; } = details;
}
