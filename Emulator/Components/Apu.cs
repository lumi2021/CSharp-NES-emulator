using System.Runtime.InteropServices;
using Emulator.Components.Core;
using ImGuiNET;
using Silk.NET.OpenAL;

namespace Emulator.Components;

public class Apu : Component
{
    private const int SampleRate = 44100;
    private const int SamplesPerBuffer = 735; // ~16.6ms
    
    private int cycleCounter = 0;
    private int frameCounter = 0;
    
    private bool DMCActive = false;
    private bool DMCInterrupt = false;
    private bool FrameInterrupt = false;

    private PulseChannel Pulse1 = new();
    private PulseChannel Pulse2 = new();
    private TriangleChannel Triangle = new();
    private NoiseChannel Noise = new();
    private DMCChannel DMC = new();

    static readonly byte[] LengthTable = [
        10, 254, 20,  2, 40,  4, 80,  6,
        160, 8, 60, 10, 14, 12, 26, 14,
        12, 16, 24, 18, 48, 20, 96, 22,
        192, 24, 72, 26, 16, 28, 32, 30
    ];
    static readonly ushort[] NoisePeriodTable = [
        4, 8, 16, 32, 64, 96, 128, 160,
        202, 254, 380, 508, 762, 1016, 2034, 4068,
    ];
    static readonly ushort[] DmcPeriodTable = [
        428, 380, 340, 320,
        286, 254, 226, 214,
        190, 160, 142, 128,
        106,  85,  72,  54
    ];
    static readonly byte[][] DutyTable = [
        [ 0, 1, 0, 0, 0, 0, 0, 0 ], // 12.5% 
        [ 0, 1, 1, 0, 0, 0, 0, 0 ], // 25%
        [ 0, 1, 1, 1, 1, 0, 0, 0 ], // 50%
        [ 1, 0, 0, 1, 1, 1, 1, 1 ], // 75%
    ];
    static readonly byte[] TriangleTable = [
        15, 14, 13, 12, 11, 10,  9,  8,  7,  6,  5,  4,  3,  2,  1,  0,
        0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15,
    ];
    
    private double audioTimer = 0;
    private const double CyclesPerSample = 1789773.0 / SampleRate; 
    private readonly List<short> sampleBuffer = new(SamplesPerBuffer);
    
    private AL al;
    private uint source;
    private const int BufferCount = 3;
    private uint[] buffers = new uint[BufferCount];
    private Queue<uint> availableBuffers = new();
    
    public unsafe Apu(VirtualSystem sys) : base(sys)
    {
        Program.DrawPopup += DebugAPU;
        
        al = AL.GetApi();
        var alc = ALContext.GetApi();

        var dev = alc.OpenDevice(null);
        if (dev == null) return;
        
        var ctx = alc.CreateContext(dev, null);
        alc.MakeContextCurrent(ctx);
        
        source = al.GenSource();
            
        for (var i = 0; i < BufferCount; i++) 
        {
            buffers[i] = al.GenBuffer();
            availableBuffers.Enqueue(buffers[i]);
        }
    }

    public void Step(int ticks)
    {
        Pulse1.Process(ticks);
        Pulse2.Process(ticks);
        Triangle.Process(ticks);
        Noise.Process(ticks);
        DMC.Process(ticks);
        
        frameCounter += ticks;
        while (frameCounter >= 7457)
        {
            ClockFrameSequencer();
            frameCounter -= 7457;
        }
        
        audioTimer += ticks;
        while (audioTimer >= CyclesPerSample)
        {
            GenerateSample();
            audioTimer -= CyclesPerSample;
        }
    }
    private void ClockFrameSequencer()
    {
        //Pulse1.ClockEnvelope();
        //Pulse2.ClockEnvelope();
        Noise.ClockEnvelope();

        //Pulse1.ClockLength();
        //Pulse2.ClockLength();
        //Triangle.ClockLength();
        //Noise.ClockLength();
    }
    
    private void GenerateSample()
    {
        var p1 = Pulse1.ForceMute ? 0 : Pulse1.GetSample();
        var p2 = Pulse2.ForceMute ? 0 : Pulse2.GetSample();
        var t = Triangle.ForceMute ? 0 : Triangle.GetSample();
        var n = Noise.ForceMute ? 0 : Noise.GetSample();
        var d = DMC.ForceMute ? 0 : DMC.GetSample();
        
        var pulseOut = (p1 + p2 == 0.0f) ? 0.0f : 95.88f / ((8128.0f / (p1 + p2)) + 100.0f);
        var tndOut = (t + n + d == 0.0f) ? 0.0f : 159.79f / ((1.0f / (t / 8227.0f + n / 12241.0f + d / 22638.0f)) + 100.0f);

        var totalOutput = pulseOut + tndOut; 
        
        var clampedOutput = Math.Clamp(totalOutput, 0.0f, 1.0f);

        var finalSample = (short)((clampedOutput - 0.5f) * 2.0f * short.MaxValue);
        sampleBuffer.Add(finalSample);
        
        if (sampleBuffer.Count >= SamplesPerBuffer) QueueAudio();
    }
    
    private unsafe void QueueAudio()
    {
        al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out var processed);
        while (processed > 0)
        {
            uint unqueuedBuffer;
            al.SourceUnqueueBuffers(source, 1, &unqueuedBuffer);
            availableBuffers.Enqueue(unqueuedBuffer);
            processed--;
        }
        
        if (availableBuffers.Count > 0)
        {
            uint bufferId = availableBuffers.Dequeue();
            fixed (short* ptr = CollectionsMarshal.AsSpan(sampleBuffer))
            {
                al.BufferData(bufferId, BufferFormat.Mono16, ptr, sampleBuffer.Count * sizeof(short), SampleRate);
            }

            al.SourceQueueBuffers(source, 1, &bufferId);
        }
        
        al.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
        if ((SourceState)state != SourceState.Playing)
        {
            al.SourcePlay(source);
        }
        
        sampleBuffer.Clear();
    }
    
    public byte ReadStatus()
    {
        var value = 0;

        value |= 0b_10000000 * (DMCInterrupt ? 1 : 0);
        value |= 0b_01000000 * (FrameInterrupt ? 1 : 0);
        value |= 0b_00010000 * (DMCActive ? 1 : 0);

        value |= 0b_00001000 * (Noise.LengthCounter > 0 ? 1 : 0);
        value |= 0b_00000100 * (Triangle.LengthCounter > 0 ? 1 : 0);
        value |= 0b_00000010 * (Pulse2.LengthCounter > 0 ? 1 : 0);
        value |= 0b_00000001 * (Pulse1.LengthCounter > 0 ? 1 : 0);

        FrameInterrupt = false;
        DMCInterrupt = false;
        
        return (byte)value;
    }
    public void Write(ushort addr, byte value)
    {
        switch (addr)
        {
            // Pulse 1
            case 0x4000: // Duty / Envelope
                Pulse1.Duty = (value >> 6) & 3;
                Pulse1.ConstantVolume = (value & 0x10) != 0;
                Pulse1.Volume = value & 0x0F;
            break;
            
            case 0x4001: // Sweep
            break;
            
            case 0x4002: // Timer low
                Pulse1.Timer = (Pulse1.Timer & 0xFF00) | value;
                Pulse1.TimerCounter = Pulse1.Timer;
            break;
            
            case 0x4003: // Timer high / length counter load
                Pulse1.Timer = (Pulse1.Timer & 0x00FF) | ((value & 0x07) << 8);
                Pulse1.LengthCounter = LengthTable[value >> 3];
                Pulse1.DutyStep = 0;
                Pulse1.EnvelopeStartFlag = true;
                Pulse1.TimerCounter = Pulse1.Timer;
            break;

            // Pulse 2
            case 0x4004:
                Pulse2.Duty = (value >> 6) & 3;
                Pulse2.ConstantVolume = (value & 0x10) != 0;
                Pulse2.Volume = value & 0x0F;
            break;
            
            case 0x4005: // Sweep
            break;
            
            case 0x4006: // Timer low
                Pulse2.Timer = (Pulse2.Timer & 0xFF00) | value;
                Pulse2.TimerCounter = Pulse2.Timer;
                break;
            
            case 0x4007: // Timer high / length counter load
                Pulse2.Timer = (Pulse2.Timer & 0x00FF) | ((value & 0x07) << 8);
                Pulse2.LengthCounter = LengthTable[value >> 3];
                Pulse2.DutyStep = 0;
                Pulse2.EnvelopeStartFlag = true;
                Pulse2.TimerCounter = Pulse2.Timer;
            break;

            // Triangle
            case 0x4008: // Linear counter control
                Triangle.LinearCounterReload = (value & 0x80) != 0;
                Triangle.LinearCounter = value & 0x7F;
            break;
            
            case 0x4009: // Ignored address
            break;
            
            case 0x400A: // Timer low
                Triangle.Timer = (Triangle.Timer & 0xFF00) | value;
            break;
            
            case 0x400B: // Timer high / length counter load
                Triangle.Timer = (Triangle.Timer & 0x00FF) | ((value & 0x07) << 8);
                Triangle.LengthCounter = LengthTable[value >> 3];
                Triangle.SequenceStep = 0;
            break;

            // Noise
            case 0x400C: // Envelope / constant volume
                Noise.ConstantVolume = (value & 0x10) != 0;
                Noise.Volume         = value & 0x0F;
                Noise.EnvelopeLoop   = (value & 0x20) != 0;
            break;
            
            case 0x400D: break; // Ignored address
            
            case 0x400E: // Mode / period
                Noise.Mode = (value & 0x80) != 0;
                Noise.Timer = NoisePeriodTable[value & 0x0F];
            break;
            
            case 0x400F: // Length counter load
                Noise.LengthCounter = LengthTable[value >> 3];
                Noise.EnvelopeStartFlag = true;
            break;

            // DMC
            case 0x4010: // Control
                DMC.Enabled = (value & 0x80) != 0;
                DMCInterrupt = (value & 0x40) != 0;
                DMC.Timer = DmcPeriodTable[value & 0x0F];
            break;
            
            case 0x4011: // Direct output level
                DMC.OutputLevel = value & 0x7F;
            break;
            
            case 0x4012: // Sample address
            break;
            
            case 0x4013: // Sample length
            break;

            case 0x4015: // Channel enable / status clear
                Pulse1.Enabled = (value & 0x01) != 0;
                Pulse2.Enabled = (value & 0x02) != 0;
                Triangle.Enabled = (value & 0x04) != 0;
                Noise.Enabled = (value & 0x08) != 0;
                DMC.Enabled = (value & 0x10) != 0;
            break;

            case 0x4017: // Frame counter
                FrameInterrupt = (value & 0x40) != 0;
            break;

            default:
                Console.WriteLine($"Warning: Attempted write to invalid APU address {addr:X4}");
            break;
        }
    }
    
    private void DebugAPU()
    {
        ImGui.Begin("APU Debug");
        ImGui.Text($"Cycle: {cycleCounter}");
        ImGui.Text($"Frame: {frameCounter}");
        
        ImGui.SeparatorText("Global Flags:");
        
        ImGui.TextDisabled($"DMC Active:"); ImGui.SameLine();
        if (DMCActive) ImGui.Text("True"); else ImGui.TextDisabled("False");
        
        ImGui.TextDisabled($"DMC Interrupt:"); ImGui.SameLine();
        if (DMCInterrupt) ImGui.Text("True"); else ImGui.TextDisabled("False");
        
        ImGui.TextDisabled($"Frame Interrupt:"); ImGui.SameLine();
        if (FrameInterrupt) ImGui.Text("True"); else ImGui.TextDisabled("False");
        
        ImGui.SeparatorText("Cannels");

        if (ImGui.BeginTable("APUChannels", 8, ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("M");
            ImGui.TableSetupColumn("Channel");
            ImGui.TableSetupColumn("Enabled");
            ImGui.TableSetupColumn("Duty/Mode");
            ImGui.TableSetupColumn("Timer");
            ImGui.TableSetupColumn("TimerCounter");
            ImGui.TableSetupColumn("Volume/Linear");
            ImGui.TableSetupColumn("Length");

            ImGui.TableHeadersRow();

            void DrawPulse(string name, PulseChannel p, int idx)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Checkbox("P" + idx, ref p.ForceMute);
                
                ImGui.TableNextColumn(); ImGui.Text(name);
                ImGui.TableNextColumn(); ImGui.Text(p.Enabled.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.Duty.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.Timer.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.TimerCounter.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.Volume.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.LengthCounter.ToString());
                //ImGui.TableNextColumn(); ImGui.Text(p.GetSample().ToString(CultureInfo.InvariantCulture));
            }

            DrawPulse("Pulse 1", Pulse1, 1);
            DrawPulse("Pulse 2", Pulse2, 2);

            // Triangle
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Checkbox("T", ref Triangle.ForceMute);
            
            ImGui.TableNextColumn(); ImGui.Text("Triangle");
            ImGui.TableNextColumn(); ImGui.Text(Triangle.Enabled.ToString());
            ImGui.TableNextColumn(); ImGui.Text("-");
            ImGui.TableNextColumn(); ImGui.Text(Triangle.Timer.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Triangle.TimerCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Triangle.LinearCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Triangle.LengthCounter.ToString());
            //ImGui.TableNextColumn(); ImGui.Text(Triangle.GetSample().ToString(CultureInfo.InvariantCulture));

            // Noise
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Checkbox("N", ref Noise.ForceMute);
            
            ImGui.TableNextColumn(); ImGui.Text("Noise");
            ImGui.TableNextColumn(); ImGui.Text(Noise.Enabled.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.Mode.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.Timer.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.TimerCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.Volume.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.LengthCounter.ToString());
            //ImGui.TableNextColumn(); ImGui.Text(Noise.GetSample().ToString(CultureInfo.InvariantCulture));
            
            // DMC
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Checkbox("D", ref DMC.ForceMute);
            
            ImGui.TableNextColumn(); ImGui.Text("DMC");
            ImGui.TableNextColumn(); ImGui.Text(DMC.Enabled.ToString());
            ImGui.TableNextColumn(); ImGui.Text("-");
            ImGui.TableNextColumn(); ImGui.Text(DMC.Timer.ToString());
            ImGui.TableNextColumn(); ImGui.Text(DMC.TimerCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(DMC.OutputLevel.ToString());
            ImGui.TableNextColumn(); ImGui.Text(DMC.SampleData?.Length.ToString() ?? "null");
            //ImGui.TableNextColumn(); ImGui.Text(DMC.GetSample().ToString(CultureInfo.InvariantCulture));
                
            ImGui.EndTable();
        }
            
        ImGui.End();
    }
    
    private class PulseChannel
    {
        public bool ForceMute;
        public bool Enabled;
        
        public int Duty;
        
        public int Timer;
        public int TimerCounter;
        
        public int DutyStep;
        
        public int Volume = 0;
        public bool ConstantVolume;
        public int EnvelopeCounter;
        public bool EnvelopeStartFlag;
        
        public int LengthCounter;
        
        public void Process(int ticks)
        {
            if (!Enabled) return;
            
            TimerCounter -= ticks;
            while (TimerCounter < 0)
            {
                TimerCounter += Timer + 1;
                DutyStep = (DutyStep + 1) & 7;
            }
        }
        
        public float GetSample()
        {
            if (!Enabled || LengthCounter == 0 || Timer < 8) return 0;
            int waveStep = DutyTable[Duty][DutyStep];
            if (waveStep == 0) return 0;
            return Volume; 
        }
    }
    private class TriangleChannel
    {
        public bool ForceMute;
        public bool Enabled;
        
        public int Timer;
        public int TimerCounter;
        
        public int SequenceStep;
        
        public int LinearCounter;
        public bool LinearCounterReload;

        public int LengthCounter;
        
        public void Process(int ticks)
        {
            if (!Enabled) return;
            if (LengthCounter == 0) return;
            if (LinearCounter == 0) return;

            TimerCounter -= ticks;
            while (TimerCounter < 0)
            {
                TimerCounter += Timer + 1;
                SequenceStep = (SequenceStep + 1) & 31;
            }
        }
        
        public float GetSample()
        {
            if (!Enabled || LengthCounter == 0 || LinearCounter == 0 || Timer < 2) 
                return 0;
            return TriangleTable[SequenceStep];
        }
    }
    private class NoiseChannel
    {
        public bool ForceMute;
        public bool Enabled;
        
        private ushort Lfsr = 1;
        
        public int Timer;
        public int TimerCounter;
        
        public int Volume;
        public bool ConstantVolume;
        public int EnvelopeCounter;
        public bool EnvelopeStartFlag;
        
        public int EnvelopeDivider;
        public int EnvelopeDecay = 15;
        public bool EnvelopeLoop;
        
        public int LengthCounter;
        
        public bool Mode;
        
        public void Process(int ticks)
        {
            if (!Enabled) return;

            TimerCounter -= ticks;
            while (TimerCounter < 0)
            {
                TimerCounter += Timer + 1;
                
                var sourceBit = Mode ? 6 : 1;
                var feedback = (Lfsr & 1) ^ ((Lfsr >> sourceBit) & 1);
                
                Lfsr >>= 1;
                Lfsr |= (ushort)(feedback << 14);
            }
        }
        
        public void ClockEnvelope()
        {
            if (EnvelopeStartFlag)
            {
                EnvelopeStartFlag = false;

                EnvelopeDecay   = 15;
                EnvelopeDivider = Volume;
                return;
            }

            if (EnvelopeDivider > 0)
            {
                EnvelopeDivider--;
                return;
            }

            EnvelopeDivider = Volume;

            if (EnvelopeDecay > 0)
            {
                EnvelopeDecay--;
            }
            else if (EnvelopeLoop)
            {
                EnvelopeDecay = 15;
            }
        }
        
        public float GetSample()
        {
            if (!Enabled || LengthCounter == 0) return 0;
            if ((Lfsr & 1) != 0) return 0;

            return (ConstantVolume ? Volume : EnvelopeDecay) * 0.5f;
        }
    }
    private class DMCChannel
    {
        public bool ForceMute;
        public bool Enabled;
        
        public byte[] SampleData;
        public int SampleIndex;
        
        public int Timer;
        public int TimerCounter;
        
        public int OutputLevel;
        
        public void Process(int ticks)
        {
            if (!Enabled) return;
            TimerCounter -= ticks;

            while (TimerCounter <= 0) TimerCounter += Timer + 1;

            if (SampleData == null || SampleIndex >= SampleData.Length) return;
            var sample = SampleData[SampleIndex++];
            OutputLevel = sample & 0x7F;
        }
        
        public float GetSample()
        {
            return OutputLevel;
        }
    }

}
