// Copyright (C) 2026 Term Organize contributors.
// SPDX-License-Identifier: AGPL-3.0-only
//
// Apple LZFSE block format. The FSE layout and the L/M/D extra-bit tables follow
// https://github.com/lzfse/lzfse (BSD-3-Clause, Copyright (c) 2015-2016, Apple Inc.).

using System.Numerics;

namespace TermOrganize.Core;

/// <summary>
/// Decodes Apple LZFSE streams (the .catalog files written by the Mac app).
/// The managed decoder reads <c>bvx2</c>, <c>bvx1</c>, and uncompressed <c>bvx-</c> blocks.
/// Multi-byte fields are little-endian. Frequency tables use the 64-bit FSE bitstream
/// the 64-bit Mac encoder writes, on every host. The reference <c>lzfse</c> tool is
/// used only when it is installed and the managed decoder rejects a block.
/// </summary>
public static class LzfseDecoder
{
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        try
        {
            return LzfseManaged.Decompress(data);
        }
        catch (InvalidDataException) when (ToolAvailable())
        {
            return DecompressWithTool(data.ToArray());
        }
    }

    private static bool ToolAvailable() =>
        File.Exists("/tmp/lzfse/build/bin/lzfse") || File.Exists("/usr/local/bin/lzfse") || File.Exists("/usr/bin/lzfse");

    private static byte[] DecompressWithTool(byte[] data)
    {
        var tool = File.Exists("/tmp/lzfse/build/bin/lzfse") ? "/tmp/lzfse/build/bin/lzfse"
            : File.Exists("/usr/local/bin/lzfse") ? "/usr/local/bin/lzfse" : "/usr/bin/lzfse";
        var input = Path.GetTempFileName();
        var output = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(input, data);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool, $"-decode -i \"{input}\" -o \"{output}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            }) ?? throw new InvalidDataException("Could not start lzfse.");
            process.WaitForExit(30_000);
            if (process.ExitCode != 0)
                throw new InvalidDataException(process.StandardError.ReadToEnd());
            return File.ReadAllBytes(output);
        }
        finally
        {
            TryDelete(input);
            TryDelete(output);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}

/// <summary>Managed LZFSE decoder. Bytes are read little-endian; the FSE stream is the 64-bit variant.</summary>
internal static class LzfseManaged
{
    private const int LiteralsPerBlock = 40_000;
    private const int MatchesPerBlock = 10_000;
    private const int LSymbols = 20;
    private const int MSymbols = 20;
    private const int DSymbols = 64;
    private const int LiteralSymbols = 256;
    private const int LStates = 64;
    private const int MStates = 64;
    private const int DStates = 256;
    private const int LiteralStates = 1024;
    private const int FreqSymbols = LSymbols + MSymbols + DSymbols + LiteralSymbols;

    private static readonly uint MagicV2 = Magic("bvx2");
    private static readonly uint MagicV1 = Magic("bvx1");
    private static readonly uint MagicLzvn = Magic("bvxn");
    private static readonly uint MagicRaw = Magic("bvx-");
    private static readonly uint MagicEnd = Magic("bvx$");

    private static readonly byte[] LExtra = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 5, 8];
    private static readonly int[] LBase = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 20, 28, 60];
    private static readonly byte[] MExtra = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 5, 8, 11];
    private static readonly int[] MBase = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 24, 56, 312];
    private static readonly byte[] DExtra =
    [
        0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
        4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7,
        8, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 11, 11, 11, 11,
        12, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15,
    ];
    private static readonly int[] DBase =
    [
        0, 1, 2, 3, 4, 6, 8, 10, 12, 16,
        20, 24, 28, 36, 44, 52, 60, 76, 92, 108,
        124, 156, 188, 220, 252, 316, 380, 444, 508, 636,
        764, 892, 1020, 1276, 1532, 1788, 2044, 2556, 3068, 3580,
        4092, 5116, 6140, 7164, 8188, 10236, 12284, 14332, 16380, 20476,
        24572, 28668, 32764, 40956, 49148, 57340, 65532, 81916, 98300, 114684,
        131068, 163836, 196604, 229372,
    ];
    private static readonly sbyte[] FreqNBits =
    [
        2, 3, 2, 5, 2, 3, 2, 8, 2, 3, 2, 5, 2, 3, 2, 14,
        2, 3, 2, 5, 2, 3, 2, 8, 2, 3, 2, 5, 2, 3, 2, 14,
    ];
    private static readonly sbyte[] FreqValueTable =
    [
        0, 2, 1, 4, 0, 3, 1, -1, 0, 2, 1, 5, 0, 3, 1, -1,
        0, 2, 1, 6, 0, 3, 1, -1, 0, 2, 1, 7, 0, 3, 1, -1,
    ];

    public static byte[] Decompress(ReadOnlySpan<byte> src)
    {
        var dst = new byte[Math.Max(64, src.Length)];
        var written = 0;
        var pos = 0;
        while (pos + 4 <= src.Length)
        {
            var magic = ReadU32(src, pos);
            if (magic == MagicEnd) return dst[..written];
            if (magic == MagicRaw)
            {
                pos = CopyRaw(src, pos, ref dst, ref written);
                continue;
            }
            if (magic == MagicV2)
            {
                pos = DecodeV2(src, pos, ref dst, ref written);
                continue;
            }
            if (magic == MagicV1)
            {
                pos = DecodeV1(src, pos, ref dst, ref written);
                continue;
            }
            if (magic == MagicLzvn)
                throw new InvalidDataException("Managed LZFSE does not decode LZVN (bvxn) blocks.");
            throw new InvalidDataException("Not an LZFSE stream.");
        }
        throw new InvalidDataException("LZFSE stream is missing the end marker.");
    }

    private static int CopyRaw(ReadOnlySpan<byte> src, int pos, ref byte[] dst, ref int written)
    {
        if (pos + 8 > src.Length) throw new InvalidDataException("Truncated raw LZFSE block.");
        var n = ReadU32(src, pos + 4);
        pos += 8;
        if (n > src.Length - pos) throw new InvalidDataException("Raw LZFSE block exceeds the buffer.");
        Ensure(ref dst, written + (int)n);
        src.Slice(pos, (int)n).CopyTo(dst.AsSpan(written));
        written += (int)n;
        return pos + (int)n;
    }

    private static int DecodeV2(ReadOnlySpan<byte> src, int pos, ref byte[] dst, ref int written)
    {
        if (pos + 32 > src.Length) throw new InvalidDataException("Truncated LZFSE v2 header.");
        var nRaw = ReadU32(src, pos + 4);
        var v0 = ReadU64(src, pos + 8);
        var v1 = ReadU64(src, pos + 16);
        var v2 = ReadU64(src, pos + 24);
        var header = new BlockHeader
        {
            NRaw = nRaw,
            NLiterals = Field(v0, 0, 20),
            NLiteralPayload = Field(v0, 20, 20),
            LiteralBits = (int)Field(v0, 60, 3) - 7,
            NMatches = Field(v0, 40, 20),
            LiteralState0 = (ushort)Field(v1, 0, 10),
            LiteralState1 = (ushort)Field(v1, 10, 10),
            LiteralState2 = (ushort)Field(v1, 20, 10),
            LiteralState3 = (ushort)Field(v1, 30, 10),
            NLmdPayload = Field(v1, 40, 20),
            LmdBits = (int)Field(v1, 60, 3) - 7,
            LState = (ushort)Field(v2, 32, 10),
            MState = (ushort)Field(v2, 42, 10),
            DState = (ushort)Field(v2, 52, 10),
        };
        var headerSize = (int)Field(v2, 0, 32);
        if (headerSize < 32 || pos + headerSize > src.Length)
            throw new InvalidDataException("LZFSE v2 header exceeds the buffer.");
        var freq = DecodeFreq(src, pos + 32, pos + headerSize);
        return Execute(src, pos, headerSize, header, freq, ref dst, ref written);
    }

    private static int DecodeV1(ReadOnlySpan<byte> src, int pos, ref byte[] dst, ref int written)
    {
        const int headerSize = 772;
        if (pos + headerSize > src.Length) throw new InvalidDataException("Truncated LZFSE v1 header.");
        var header = new BlockHeader
        {
            NRaw = ReadU32(src, pos + 4),
            NLiterals = ReadU32(src, pos + 12),
            NMatches = ReadU32(src, pos + 16),
            NLiteralPayload = ReadU32(src, pos + 20),
            NLmdPayload = ReadU32(src, pos + 24),
            LiteralBits = (int)ReadU32(src, pos + 28),
            LiteralState0 = ReadU16(src, pos + 32),
            LiteralState1 = ReadU16(src, pos + 34),
            LiteralState2 = ReadU16(src, pos + 36),
            LiteralState3 = ReadU16(src, pos + 38),
            LmdBits = (int)ReadU32(src, pos + 40),
            LState = ReadU16(src, pos + 44),
            MState = ReadU16(src, pos + 46),
            DState = ReadU16(src, pos + 48),
        };
        var freq = new ushort[FreqSymbols];
        for (var i = 0; i < FreqSymbols; i++) freq[i] = ReadU16(src, pos + 50 + i * 2);
        return Execute(src, pos, headerSize, header, freq, ref dst, ref written);
    }

    private static int Execute(ReadOnlySpan<byte> src, int pos, int headerSize, BlockHeader header, ushort[] freq, ref byte[] dst, ref int written)
    {
        if (header.NLiterals > LiteralsPerBlock || header.NMatches > MatchesPerBlock)
            throw new InvalidDataException("LZFSE block is larger than the format allows.");
        if ((header.NLiterals & 3) != 0)
            throw new InvalidDataException("LZFSE literal count is not a multiple of 4.");
        if (header.LiteralState0 >= LiteralStates || header.LiteralState1 >= LiteralStates
            || header.LiteralState2 >= LiteralStates || header.LiteralState3 >= LiteralStates
            || header.LState >= LStates || header.MState >= MStates || header.DState >= DStates)
            throw new InvalidDataException("LZFSE decoder state is out of range.");
        var payload = (long)header.NLiteralPayload + header.NLmdPayload;
        if (payload < 0 || pos + headerSize + payload > src.Length)
            throw new InvalidDataException("LZFSE payload exceeds the buffer.");

        var lFreq = freq.AsSpan(0, LSymbols);
        var mFreq = freq.AsSpan(LSymbols, MSymbols);
        var dFreq = freq.AsSpan(LSymbols + MSymbols, DSymbols);
        var litFreq = freq.AsSpan(LSymbols + MSymbols + DSymbols, LiteralSymbols);
        var litTable = InitLiteral(litFreq, LiteralStates);
        var lTable = InitValue(lFreq, LStates, LExtra, LBase);
        var mTable = InitValue(mFreq, MStates, MExtra, MBase);
        var dTable = InitValue(dFreq, DStates, DExtra, DBase);

        var litEnd = pos + headerSize + (int)header.NLiteralPayload;
        var reader = new BitReader();
        var buf = Init(ref reader, header.LiteralBits, litEnd, 0, src);
        var literals = new byte[header.NLiterals];
        var state0 = header.LiteralState0;
        var state1 = header.LiteralState1;
        var state2 = header.LiteralState2;
        var state3 = header.LiteralState3;
        for (var i = 0; i < header.NLiterals; i += 4)
        {
            buf = Flush(ref reader, buf, 0, src);
            literals[i] = DecodeSymbol(ref state0, litTable, ref reader);
            literals[i + 1] = DecodeSymbol(ref state1, litTable, ref reader);
            literals[i + 2] = DecodeSymbol(ref state2, litTable, ref reader);
            literals[i + 3] = DecodeSymbol(ref state3, litTable, ref reader);
        }

        var lmdStart = litEnd;
        var lmdEnd = litEnd + (int)header.NLmdPayload;
        reader = new BitReader();
        buf = Init(ref reader, header.LmdBits, lmdEnd, lmdStart, src);
        var lState = header.LState;
        var mState = header.MState;
        var dState = header.DState;
        var lit = 0;
        var distance = -1;
        var produced = written;
        Ensure(ref dst, written + (int)header.NRaw);
        for (var remaining = header.NMatches; remaining > 0; remaining--)
        {
            buf = Flush(ref reader, buf, 0, src);
            var literal = DecodeValue(ref lState, lTable, ref reader);
            var match = DecodeValue(ref mState, mTable, ref reader);
            var newDistance = DecodeValue(ref dState, dTable, ref reader);
            if (newDistance != 0) distance = newDistance;
            if (literal < 0 || match < 0 || distance < 0 || (long)distance > (long)written + literal || lit + literal > literals.Length)
                throw new InvalidDataException("LZFSE match is out of range.");
            literals.AsSpan(lit, literal).CopyTo(dst.AsSpan(written));
            written += literal;
            lit += literal;
            if (match == 0) continue;
            if (distance >= match)
                dst.AsSpan(written - distance, match).CopyTo(dst.AsSpan(written));
            else
                for (var i = 0; i < match; i++) dst[written + i] = dst[written - distance + i];
            written += match;
        }
        if ((uint)(written - produced) != header.NRaw)
            throw new InvalidDataException($"LZFSE size {written - produced} did not match header {header.NRaw}.");
        return lmdEnd;
    }

    private readonly struct BlockHeader
    {
        public uint NRaw { get; init; }
        public uint NLiterals { get; init; }
        public uint NMatches { get; init; }
        public uint NLiteralPayload { get; init; }
        public uint NLmdPayload { get; init; }
        public int LiteralBits { get; init; }
        public ushort LiteralState0 { get; init; }
        public ushort LiteralState1 { get; init; }
        public ushort LiteralState2 { get; init; }
        public ushort LiteralState3 { get; init; }
        public int LmdBits { get; init; }
        public ushort LState { get; init; }
        public ushort MState { get; init; }
        public ushort DState { get; init; }
    }

    private readonly record struct LiteralEntry(byte K, byte Symbol, short Delta);
    private readonly record struct ValueEntry(byte TotalBits, byte ValueBits, short Delta, int VBase);

    private struct BitReader
    {
        public ulong Accum;
        public int Bits;
    }

    private static ushort[] DecodeFreq(ReadOnlySpan<byte> src, int start, int end)
    {
        var dst = new ushort[FreqSymbols];
        if (start == end) return dst;
        uint accum = 0;
        var accumBits = 0;
        var cursor = start;
        for (var i = 0; i < FreqSymbols; i++)
        {
            while (cursor < end && accumBits + 8 <= 32)
            {
                accum |= (uint)src[cursor] << accumBits;
                accumBits += 8;
                cursor++;
            }
            var value = FreqValue(accum, out var nbits);
            if (nbits > accumBits) throw new InvalidDataException("Truncated LZFSE frequency table.");
            dst[i] = (ushort)value;
            accum >>= nbits;
            accumBits -= nbits;
        }
        if (accumBits >= 8 || cursor != end)
            throw new InvalidDataException("LZFSE frequency table did not end on the header boundary.");
        return dst;
    }

    private static int FreqValue(uint bits, out int nbits)
    {
        var b = bits & 31;
        var n = FreqNBits[b];
        nbits = n;
        if (n == 8) return 8 + (int)((bits >> 4) & 0xf);
        if (n == 14) return 24 + (int)((bits >> 4) & 0x3ff);
        return FreqValueTable[b];
    }

    private static LiteralEntry[] InitLiteral(ReadOnlySpan<ushort> freq, int nstates)
    {
        var table = new LiteralEntry[nstates];
        var nClz = Clz(nstates);
        var slot = 0;
        var sum = 0;
        for (var i = 0; i < freq.Length; i++)
        {
            var f = freq[i];
            if (f == 0) continue;
            sum += f;
            if (sum > nstates) throw new InvalidDataException("LZFSE frequency table exceeds its state count.");
            var k = Clz(f) - nClz;
            var j0 = ((2 * nstates) >> k) - f;
            for (var j = 0; j < f; j++)
            {
                byte width;
                short delta;
                if (j < j0)
                {
                    width = (byte)k;
                    delta = unchecked((short)(((f + j) << k) - nstates));
                }
                else
                {
                    width = (byte)(k - 1);
                    delta = unchecked((short)((j - j0) << (k - 1)));
                }
                table[slot++] = new LiteralEntry(width, (byte)i, delta);
            }
        }
        return table;
    }

    private static ValueEntry[] InitValue(ReadOnlySpan<ushort> freq, int nstates, byte[] extraBits, int[] bases)
    {
        var table = new ValueEntry[nstates];
        var nClz = Clz(nstates);
        var slot = 0;
        for (var i = 0; i < freq.Length; i++)
        {
            var f = freq[i];
            if (f == 0) continue;
            var k = Clz(f) - nClz;
            var j0 = ((2 * nstates) >> k) - f;
            var valueBits = extraBits[i];
            for (var j = 0; j < f; j++)
            {
                byte width;
                short delta;
                if (j < j0)
                {
                    width = (byte)k;
                    delta = unchecked((short)(((f + j) << k) - nstates));
                }
                else
                {
                    width = (byte)(k - 1);
                    delta = unchecked((short)((j - j0) << (k - 1)));
                }
                table[slot++] = new ValueEntry((byte)(width + valueBits), valueBits, delta, bases[i]);
            }
        }
        return table;
    }

    private static int Init(ref BitReader reader, int n, int buf, int bufStart, ReadOnlySpan<byte> src)
    {
        if (n != 0)
        {
            if (buf < bufStart + 8) throw new InvalidDataException("LZFSE bitstream underrun.");
            buf -= 8;
            reader.Accum = ReadU64(src, buf);
            reader.Bits = n + 64;
        }
        else
        {
            if (buf < bufStart + 7) throw new InvalidDataException("LZFSE bitstream underrun.");
            buf -= 7;
            reader.Accum = ReadU64(src, buf) & 0x00ffffffffffffffUL;
            reader.Bits = n + 56;
        }
        if (reader.Bits < 56 || reader.Bits >= 64 || (reader.Accum >> reader.Bits) != 0)
            throw new InvalidDataException("LZFSE bitstream header is corrupt.");
        return buf;
    }

    private static int Flush(ref BitReader reader, int buf, int bufStart, ReadOnlySpan<byte> src)
    {
        var nbits = (63 - reader.Bits) & -8;
        var next = buf - (nbits >> 3);
        if (next < bufStart) throw new InvalidDataException("LZFSE bitstream underrun.");
        if (nbits > 0)
        {
            var incoming = ReadU64(src, next) & ((1UL << nbits) - 1);
            reader.Accum = (reader.Accum << nbits) | incoming;
            reader.Bits += nbits;
        }
        if (reader.Bits < 56 || reader.Bits >= 64 || (reader.Accum >> reader.Bits) != 0)
            throw new InvalidDataException("LZFSE bitstream is corrupt.");
        return next;
    }

    private static ulong Pull(ref BitReader reader, int n)
    {
        if ((uint)n > (uint)reader.Bits) throw new InvalidDataException("LZFSE bitstream underrun.");
        reader.Bits -= n;
        var result = reader.Accum >> reader.Bits;
        reader.Accum = reader.Bits == 0 ? 0 : reader.Accum & ((1UL << reader.Bits) - 1);
        return result;
    }

    private static byte DecodeSymbol(ref ushort state, LiteralEntry[] table, ref BitReader reader)
    {
        if (state >= table.Length) throw new InvalidDataException("LZFSE literal state is out of range.");
        var entry = table[state];
        var bits = Pull(ref reader, entry.K);
        state = unchecked((ushort)(entry.Delta + (int)bits));
        return entry.Symbol;
    }

    private static int DecodeValue(ref ushort state, ValueEntry[] table, ref BitReader reader)
    {
        if (state >= table.Length) throw new InvalidDataException("LZFSE value state is out of range.");
        var entry = table[state];
        var bits = Pull(ref reader, entry.TotalBits);
        var statePart = (int)(bits >> entry.ValueBits);
        var extra = entry.ValueBits == 0 ? 0 : (int)(bits & ((1UL << entry.ValueBits) - 1));
        state = unchecked((ushort)(entry.Delta + statePart));
        return entry.VBase + extra;
    }

    private static int Clz(int value) => BitOperations.LeadingZeroCount((uint)value);

    private static void Ensure(ref byte[] dst, int needed)
    {
        if (needed <= dst.Length) return;
        var size = dst.Length;
        while (size < needed) size *= 2;
        Array.Resize(ref dst, size);
    }

    private static uint Field(ulong value, int offset, int nbits) =>
        nbits == 32 ? (uint)(value >> offset) : (uint)((value >> offset) & ((1UL << nbits) - 1));

    private static uint Magic(string text) =>
        (uint)(text[0] | (text[1] << 8) | (text[2] << 16) | (text[3] << 24));

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
    {
        ulong value = 0;
        var n = Math.Min(8, data.Length - offset);
        for (var i = 0; i < n; i++) value |= (ulong)data[offset + i] << (8 * i);
        return value;
    }
}
