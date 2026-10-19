using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoDubbing.Service.ContentGeneration;

public static class WavAudio
{
    public static GeneratedAudio FromProvider(byte[] data, string mime)
    {
        var type = mime.Split(';')[0].Trim().ToLowerInvariant();
        if (type is "audio/l16" or "audio/pcm")
        {
            var match = Regex.Match(mime, @"(?:^|;)\s*rate=(\d+)(?:;|$)", RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var rate) || rate is < 8000 or > 192000
                || data.Length == 0 || data.Length % 2 != 0
                || (Regex.IsMatch(mime, @"codec\s*=", RegexOptions.IgnoreCase)
                    && !Regex.IsMatch(mime, @"codec=pcm(?:;|$)", RegexOptions.IgnoreCase))
                || (Regex.IsMatch(mime, @"channels\s*=", RegexOptions.IgnoreCase)
                    && !Regex.IsMatch(mime, @"channels=1(?:;|$)", RegexOptions.IgnoreCase)))
                throw new GenerationException("InvalidAudioMetadata");
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Encoding.ASCII, true);
            writer.Write("RIFF"u8); writer.Write(36 + data.Length); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(data.Length); writer.Write(data);
            return new(output.ToArray(), data.Length / (rate * 2d));
        }
        if (type is not ("audio/wav" or "audio/x-wav" or "audio/wave"))
            throw new GenerationException("UnsupportedAudioFormat");
        if (data.Length < 44 || !data.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !data.AsSpan(8, 4).SequenceEqual("WAVE"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)) != data.Length - 8)
            throw new GenerationException("InvalidWav");
        uint sampleRate = 0, bytes = 0;
        ushort block = 0;
        for (var offset = 12; offset + 8 <= data.Length;)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
            if (size > data.Length - offset - 8) throw new GenerationException("InvalidWav");
            var chunk = data.AsSpan(offset + 8, (int)size);
            if (data.AsSpan(offset, 4).SequenceEqual("fmt "u8))
            {
                if (size < 16 || BinaryPrimitives.ReadUInt16LittleEndian(chunk) != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]) != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]) != 16)
                    throw new GenerationException("InvalidWav");
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                block = BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);
                if (sampleRate is < 8000 or > 192000 || block != 2
                    || BinaryPrimitives.ReadUInt32LittleEndian(chunk[8..]) != sampleRate * block)
                    throw new GenerationException("InvalidWav");
            }
            if (data.AsSpan(offset, 4).SequenceEqual("data"u8)) bytes += size;
            offset += 8 + checked((int)size) + (int)(size % 2);
        }
        if (block != 2 || sampleRate == 0 || bytes == 0 || bytes % block != 0)
            throw new GenerationException("InvalidWav");
        return new(data, bytes / (sampleRate * (double)block));
    }
}
