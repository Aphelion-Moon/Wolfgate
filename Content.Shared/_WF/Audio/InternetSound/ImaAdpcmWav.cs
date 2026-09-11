namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>
/// Reads IMA ADPCM WAV (format tag 0x11), the format internet sounds are sent in. Plain managed code, so clients can
/// decode off the main thread, unlike the engine's Ogg loader. Matches ffmpeg's decoder sample for sample.
/// </summary>
public static class ImaAdpcmWav
{
    /// <summary>
    /// Interleaved 16-bit samples ready for <c>IAudioManager.LoadAudioRaw</c>.
    /// </summary>
    public sealed record DecodedAudio(short[] Samples, int Channels, int SampleRate);

    private readonly record struct WavInfo(int Channels, int SampleRate, int BlockAlign, int DataStart, int DataEnd);

    private const int FormatImaAdpcm = 0x11;

    private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97,
        107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
        876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428,
        4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350,
        22385, 24623, 27086, 29794, 32767,
    };

    /// <summary>
    /// Length of the audio from the header alone. Null if it isn't mono or stereo IMA ADPCM WAV.
    /// </summary>
    public static TimeSpan? GetDuration(byte[] wav)
    {
        if (ReadInfo(wav) is not { } info)
            return null;

        var frames = CountFrames(info);
        return frames == 0 ? null : TimeSpan.FromSeconds(frames / (double) info.SampleRate);
    }

    /// <summary>
    /// Decodes a whole file. Null if it isn't mono or stereo IMA ADPCM WAV.
    /// </summary>
    public static DecodedAudio? Decode(byte[] wav)
    {
        if (ReadInfo(wav) is not { } info)
            return null;

        var frames = CountFrames(info);
        if (frames == 0)
            return null;

        var channels = info.Channels;
        var headerBytes = 4 * channels;
        var runBytes = 4 * channels;

        var output = new short[frames * channels];
        var predictors = new int[channels];
        var indices = new int[channels];
        var frame = 0;

        for (var block = info.DataStart; block + headerBytes <= info.DataEnd; block += info.BlockAlign)
        {
            var blockEnd = Math.Min(block + info.BlockAlign, info.DataEnd);

            // Each channel opens with its predictor (the block's first sample) and step index.
            for (var c = 0; c < channels; c++)
            {
                var header = block + 4 * c;
                predictors[c] = (short) ReadUInt16(wav, header);
                indices[c] = Math.Clamp((int) wav[header + 2], 0, StepTable.Length - 1);
                output[frame * channels + c] = (short) predictors[c];
            }

            frame++;

            // Nibbles come in 4-byte runs per channel: 8 samples of left, 8 of right, and so on.
            for (var run = block + headerBytes; run + runBytes <= blockEnd; run += runBytes)
            {
                for (var c = 0; c < channels; c++)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        var packed = wav[run + 4 * c + i / 2];
                        var nibble = (i & 1) == 0 ? packed & 0x0F : packed >> 4;
                        output[(frame + i) * channels + c] = Expand(ref predictors[c], ref indices[c], nibble);
                    }
                }

                frame += 8;
            }
        }

        return new DecodedAudio(output, channels, info.SampleRate);
    }

    private static WavInfo? ReadInfo(byte[] wav)
    {
        if (wav.Length < 12 || !IsTag(wav, 0, "RIFF") || !IsTag(wav, 8, "WAVE"))
            return null;

        var format = 0;
        var channels = 0;
        var sampleRate = 0;
        var blockAlign = 0;

        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var body = pos + 8;
            var size = ReadInt32(wav, pos + 4);

            // Streamed files can carry a bogus size; clamp to what's there.
            if (size < 0 || size > wav.Length - body)
                size = wav.Length - body;

            if (IsTag(wav, pos, "fmt ") && size >= 16)
            {
                format = ReadUInt16(wav, body);
                channels = ReadUInt16(wav, body + 2);
                sampleRate = ReadInt32(wav, body + 4);
                blockAlign = ReadUInt16(wav, body + 12);
            }
            else if (IsTag(wav, pos, "data"))
            {
                if (format != FormatImaAdpcm || channels is < 1 or > 2 || sampleRate <= 0 || blockAlign <= 4 * channels)
                    return null;

                return new WavInfo(channels, sampleRate, blockAlign, body, body + size);
            }

            // Chunks are padded to even sizes.
            pos = body + size + (size & 1);
        }

        return null;
    }

    /// <summary>
    /// Samples per channel across every block; the last block may be short.
    /// </summary>
    private static int CountFrames(WavInfo info)
    {
        var headerBytes = 4 * info.Channels;
        var frames = 0;

        for (var block = info.DataStart; block + headerBytes <= info.DataEnd; block += info.BlockAlign)
        {
            var blockEnd = Math.Min(block + info.BlockAlign, info.DataEnd);
            frames += 1 + (blockEnd - block - headerBytes) / (4 * info.Channels) * 8;
        }

        return frames;
    }

    /// <summary>
    /// One IMA step, using ffmpeg's multiply form so output is bit-exact with the encoder it pairs with.
    /// </summary>
    private static short Expand(ref int predictor, ref int index, int nibble)
    {
        var step = StepTable[index];
        var diff = ((2 * (nibble & 7) + 1) * step) >> 3;

        predictor = Math.Clamp((nibble & 8) != 0 ? predictor - diff : predictor + diff, short.MinValue, short.MaxValue);
        index = Math.Clamp(index + IndexTable[nibble], 0, StepTable.Length - 1);
        return (short) predictor;
    }

    private static bool IsTag(byte[] data, int offset, string tag)
    {
        return data[offset] == tag[0] && data[offset + 1] == tag[1] && data[offset + 2] == tag[2] && data[offset + 3] == tag[3];
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        return data[offset] | data[offset + 1] << 8;
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        return data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24;
    }
}
