using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Flip;

// Original procedural sound from v0.4.0-beta.3, riprodotto via buffer PCM pre-caricato in memoria su WinMM waveOut.
internal sealed class WheelSound : IDisposable
{
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint WaveFormatPcm = 1;
    private const uint CallbackNull = 0;
    private const uint SndAsyncMemoryNoDefault = 0x0007; // SND_SYNC(0)|SND_ASYNC(1)|SND_NODEFAULT(2)|SND_MEMORY(4)
    private const int SampleRate = 44100;
    private const int LeadInSilenceMs = 10;
    private const int TailSilenceMs = 4;
    private const int PrimeSilenceMs = 12;
    private const int IdleCloseMs = 4000;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }

    private static readonly uint WaveHeaderSize = (uint)Marshal.SizeOf<WaveHeader>();

    [DllImport("winmm.dll")]
    private static extern uint waveOutOpen(
        out nint handle,
        uint deviceId,
        ref WaveFormatEx format,
        nint callback,
        nint instance,
        uint flags);

    [DllImport("winmm.dll")]
    private static extern uint waveOutPrepareHeader(nint handle, nint header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint waveOutUnprepareHeader(nint handle, nint header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint waveOutWrite(nint handle, nint header, uint headerSize);

    [DllImport("winmm.dll")]
    private static extern uint waveOutReset(nint handle);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(nint handle);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint data, nint module, uint flags);

    private readonly object sync = new();
    private readonly byte[] playbackPcm;
    private readonly byte[] primePcm;
    private readonly GCHandle pinnedPcm;
    private readonly GCHandle pinnedPrimePcm;
    private readonly GCHandle pinnedFallbackWav;
    private readonly nint[] tickHeaderPtrs = new nint[2];
    private readonly Timer idleTimer;

    private nint primeHeaderPtr;
    private nint waveOutHandle;
    private bool headersPrepared;
    private int nextHeaderIndex;
    private int disposedFlag;
    private bool IsDisposed => Volatile.Read(ref disposedFlag) != 0;

    internal bool Enabled { get; set; }

    internal WheelSound(bool enabled)
    {
        Enabled = enabled;

        byte[] rawWave = CreateWave();
        playbackPcm = CreatePlaybackPcm(rawWave, LeadInSilenceMs, TailSilenceMs);
        primePcm = new byte[(SampleRate * PrimeSilenceMs / 1000) * 2];
        byte[] fallbackWav = CreatePlaybackWave(playbackPcm);

        pinnedPcm = GCHandle.Alloc(playbackPcm, GCHandleType.Pinned);
        pinnedPrimePcm = GCHandle.Alloc(primePcm, GCHandleType.Pinned);
        pinnedFallbackWav = GCHandle.Alloc(fallbackWav, GCHandleType.Pinned);

        int headerBytes = (int)WaveHeaderSize;
        for (int i = 0; i < tickHeaderPtrs.Length; i++)
        {
            tickHeaderPtrs[i] = Marshal.AllocHGlobal(headerBytes);
            WriteHeader(tickHeaderPtrs[i], pinnedPcm.AddrOfPinnedObject(), (uint)playbackPcm.Length);
        }

        primeHeaderPtr = Marshal.AllocHGlobal(headerBytes);
        WriteHeader(primeHeaderPtr, pinnedPrimePcm.AddrOfPinnedObject(), (uint)primePcm.Length);

        idleTimer = new Timer(OnIdleClose, null, Timeout.Infinite, Timeout.Infinite);
        Evidence.Log("sound.initialized", new { enabled, backend = "WinMM-waveOut-pinned", leadInMs = LeadInSilenceMs, pcmBytes = playbackPcm.Length });
    }

    /// <summary>
    /// Sveglia la sessione audio di Windows in background all'apertura della ruota con un breve buffer silenzioso,
    /// così il primo tick dal centro al primo spicchio suona subito a volume pieno e costante senza cold-start.
    /// </summary>
    internal void WarmUp()
    {
        if (IsDisposed || !Enabled || ScreenEdgeAuraWindow.Suppressed) return;
        _ = Task.Run(WarmUpCore);
    }

    private void WarmUpCore()
    {
        if (IsDisposed || !Enabled || ScreenEdgeAuraWindow.Suppressed) return;
        lock (sync)
        {
            if (IsDisposed || !Enabled || ScreenEdgeAuraWindow.Suppressed) return;
            bool openedNow = waveOutHandle == 0 && OpenDeviceLocked();
            if (openedNow && waveOutHandle != 0 && headersPrepared)
            {
                _ = waveOutWrite(waveOutHandle, primeHeaderPtr, WaveHeaderSize);
            }
            if (waveOutHandle != 0)
            {
                idleTimer.Change(IdleCloseMs, Timeout.Infinite);
            }
        }
    }

    internal void Play()
    {
        if (IsDisposed || !Enabled || ScreenEdgeAuraWindow.Suppressed) return;
        lock (sync)
        {
            if (IsDisposed || !Enabled || ScreenEdgeAuraWindow.Suppressed) return;

            if (TryPlayWaveOutLocked())
            {
                idleTimer.Change(IdleCloseMs, Timeout.Infinite);
                return;
            }

            // Un solo tentativo di riapertura se l'endpoint audio predefinito è cambiato.
            CloseDeviceLocked();
            if (TryPlayWaveOutLocked())
            {
                idleTimer.Change(IdleCloseMs, Timeout.Infinite);
                return;
            }

            try { PlaySound(pinnedFallbackWav.AddrOfPinnedObject(), 0, SndAsyncMemoryNoDefault); } catch { }
        }
    }

    private bool TryPlayWaveOutLocked()
    {
        if (waveOutHandle == 0 && !OpenDeviceLocked())
            return false;
        if (waveOutHandle == 0 || !headersPrepared)
            return false;

        // Interrompe subito un eventuale tick precedente: nessuna sovrapposizione né variazione di guadagno.
        _ = waveOutReset(waveOutHandle);
        nint hdr = tickHeaderPtrs[nextHeaderIndex];
        nextHeaderIndex ^= 1;
        return waveOutWrite(waveOutHandle, hdr, WaveHeaderSize) == 0;
    }

    private bool OpenDeviceLocked()
    {
        var format = new WaveFormatEx
        {
            FormatTag = (ushort)WaveFormatPcm,
            Channels = 1,
            SamplesPerSec = SampleRate,
            AvgBytesPerSec = SampleRate * 2,
            BlockAlign = 2,
            BitsPerSample = 16,
            Size = 0
        };

        if (waveOutOpen(out nint handle, WaveMapper, ref format, 0, 0, CallbackNull) != 0 || handle == 0)
            return false;

        waveOutHandle = handle;

        for (int i = 0; i < tickHeaderPtrs.Length; i++)
        {
            WriteHeader(tickHeaderPtrs[i], pinnedPcm.AddrOfPinnedObject(), (uint)playbackPcm.Length);
            if (waveOutPrepareHeader(waveOutHandle, tickHeaderPtrs[i], WaveHeaderSize) != 0)
            {
                CloseDeviceLocked();
                return false;
            }
        }

        WriteHeader(primeHeaderPtr, pinnedPrimePcm.AddrOfPinnedObject(), (uint)primePcm.Length);
        if (waveOutPrepareHeader(waveOutHandle, primeHeaderPtr, WaveHeaderSize) != 0)
        {
            CloseDeviceLocked();
            return false;
        }

        headersPrepared = true;
        return true;
    }

    private void CloseDeviceLocked()
    {
        if (waveOutHandle == 0) return;

        try { _ = waveOutReset(waveOutHandle); } catch { }
        if (headersPrepared)
        {
            for (int i = 0; i < tickHeaderPtrs.Length; i++)
            {
                try { _ = waveOutUnprepareHeader(waveOutHandle, tickHeaderPtrs[i], WaveHeaderSize); } catch { }
            }
            try { _ = waveOutUnprepareHeader(waveOutHandle, primeHeaderPtr, WaveHeaderSize); } catch { }
            headersPrepared = false;
        }

        try { _ = waveOutClose(waveOutHandle); } catch { }
        waveOutHandle = 0;
    }

    private void OnIdleClose(object? _)
    {
        if (IsDisposed || !Monitor.TryEnter(sync)) return;
        try
        {
            CloseDeviceLocked();
        }
        finally
        {
            Monitor.Exit(sync);
        }
    }

    private static void WriteHeader(nint ptr, nint dataPtr, uint length)
    {
        var hdr = new WaveHeader
        {
            Data = dataPtr,
            BufferLength = length,
            BytesRecorded = 0,
            User = 0,
            Flags = 0,
            Loops = 0,
            Next = 0,
            Reserved = 0
        };
        Marshal.StructureToPtr(hdr, ptr, fDeleteOld: false);
    }

    /// <summary>
    /// Prepara il buffer PCM16 di riproduzione anteponendo un breve silenzio morbido (10 ms) al tick originale,
    /// così il fade-in anti-pop dell'audio engine di Windows non attenua né taglia il transiente del primo tick.
    /// </summary>
    internal static byte[] CreatePlaybackPcm(byte[] rawWave, int leadInMs = LeadInSilenceMs, int tailMs = TailSilenceMs)
    {
        const int wavHeaderBytes = 44;
        int bodyLength = Math.Max(0, rawWave.Length - wavHeaderBytes);
        int leadBytes = Math.Max(0, SampleRate * leadInMs / 1000) * 2;
        int tailBytes = Math.Max(0, SampleRate * tailMs / 1000) * 2;

        byte[] pcm = new byte[leadBytes + bodyLength + tailBytes];
        if (bodyLength > 0)
        {
            Buffer.BlockCopy(rawWave, wavHeaderBytes, pcm, leadBytes, bodyLength);
        }
        return pcm;
    }

    private static byte[] CreatePlaybackWave(byte[] pcmBytes)
    {
        using var output = new MemoryStream(44 + pcmBytes.Length);
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcmBytes.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcmBytes.Length);
        writer.Write(pcmBytes);
        writer.Flush();
        return output.ToArray();
    }

    internal static byte[] CreateWave()
    {
        const int rate = SampleRate, count = 1764; // 40 ms original dry tick, mono PCM16
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
        uint noise = 0x464C4950;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / rate, u = (double)i / (count - 1);
            noise = unchecked(noise * 1664525 + 1013904223);
            double transient = (noise / (double)uint.MaxValue * 2 - 1) * Math.Exp(-t / 0.0035);
            double attack = 1 - Math.Exp(-t / 0.00035);
            double tail = Math.Pow(Math.Cos(u * Math.PI / 2), 2);
            double sample = (0.50 * transient + 0.45 * Math.Sin(2 * Math.PI * 2300 * t) * Math.Exp(-t / 0.0065) + 0.25 * Math.Sin(2 * Math.PI * 950 * t) * Math.Exp(-t / 0.009)) * attack * tail * 0.65;
            writer.Write((short)Math.Round(Math.Clamp(sample, -1, 1) * short.MaxValue));
        }
        writer.Flush(); return output.ToArray();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposedFlag, 1) != 0) return;
        try { idleTimer.Dispose(); } catch { }
        lock (sync)
        {
            CloseDeviceLocked();
            try { PlaySound(0, 0, 0); } catch { }
            for (int i = 0; i < tickHeaderPtrs.Length; i++)
            {
                if (tickHeaderPtrs[i] != 0)
                {
                    Marshal.FreeHGlobal(tickHeaderPtrs[i]);
                    tickHeaderPtrs[i] = 0;
                }
            }
            if (primeHeaderPtr != 0)
            {
                Marshal.FreeHGlobal(primeHeaderPtr);
                primeHeaderPtr = 0;
            }
            if (pinnedPcm.IsAllocated) pinnedPcm.Free();
            if (pinnedPrimePcm.IsAllocated) pinnedPrimePcm.Free();
            if (pinnedFallbackWav.IsAllocated) pinnedFallbackWav.Free();
        }
    }
}
