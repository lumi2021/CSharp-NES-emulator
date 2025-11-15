using Emulator.Components.Core;
using ImGuiNET;
using Silk.NET.OpenAL;

namespace Emulator.Components;

public class Apu : Component
{

    private AL al;
    private uint buffer;
    private uint source;
    
    private const int BufferCount = 4;
    private uint[] buffers = new uint[BufferCount];
    
    private const int SampleRate = 44100;
    private const int SamplesPerBuffer = 735; // ~16.6ms
    private short[] currentBuffer = new short[SamplesPerBuffer];
    private int bufferPos = 0;
    private int nextBuffer = 0;
    
    private int cycleCounter = 0;
    
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
        202, 254, 380, 508, 762, 1016, 2034, 4068
    ];
    static readonly ushort[] DmcPeriodTable = [
        428, 380, 340, 320,
        286, 254, 226, 214,
        190, 160, 142, 128,
        106,  85,  72,  54
    ];
    
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
        for (var i = 0; i < BufferCount; i++) buffers[i] = al.GenBuffer();
        
        al.SourcePlay(source);
    }

    public void Tick()
    {
        cycleCounter++;
        
        Pulse1.Process();
        Pulse2.Process();
        //Triangle.Process();
        //Noise.Process();
        //DMC.Process();
        
        var sample = MixChannels();
        currentBuffer[bufferPos++] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

        if (bufferPos >= SamplesPerBuffer)
        {
            al.BufferData(buffers[nextBuffer], BufferFormat.Mono16, currentBuffer, SampleRate);
            al.SourceQueueBuffers(source, [buffers[nextBuffer]]);
            nextBuffer = (nextBuffer + 1) % BufferCount;
            bufferPos = 0;
        }

        al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out var processed);
        while (processed-- > 0)
        {
            uint[] unqueued = new uint[1];
            al.SourceUnqueueBuffers(source, unqueued);
        }

        al.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
        if (state != (int)SourceState.Playing)
            al.SourcePlay(source);
    }
    
    
    public byte ReadStatus()
    {
        int value = 0;

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
                // Pode implementar sweep se quiser
                break;
            
            case 0x4002: // Timer low
                Pulse1.Timer = (Pulse1.Timer & 0xFF00) | value;
                Pulse1.TimerCounter = Pulse1.Timer;
                break;
            
            case 0x4003: // Timer high / length counter load
                Pulse1.Timer = (Pulse1.Timer & 0x00FF) | ((value & 0x07) << 8);
                Pulse1.LengthCounter = LengthTable[value >> 3]; // tabela de lengths do NES
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
                Noise.Volume = value & 0x0F;
                break;
            
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
    
    public short MixChannels()
    {
        double p1 = Pulse1.GetSample();
        double p2 = Pulse2.GetSample();
        double tri = Triangle.GetSample();
        double noi = Noise.GetSample();
        double dmc = DMC.GetSample();
        
        double pulse = 0;
        if (p1 > 0 || p2 > 0)
            pulse = 95.88 / ( (8128.0 / (p1 + p2)) + 100 );

        var tnd = 0.0;
        var t = tri / 8227.0;
        var n = noi / 12241.0;
        var d = dmc / 22638.0;

        if (t + n + d > 0)
            tnd = 159.79 / (1.0 / (t + n + d) + 100);

        double o = pulse + tnd;
        
        return (short)(o * 32767);
    }
    
    short[] GenerateBuffer()
    {
        short[] pcm = new short[SamplesPerBuffer];
        for (int i = 0; i < SamplesPerBuffer; i++)
        {
            float sample = MixChannels();
            pcm[i] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            Tick();
        }
        return pcm;
    }
    
    public void DebugAPU()
    {
        ImGui.Begin("APU Debug");
        ImGui.Text($"Cycle: {cycleCounter}");
        
        ImGui.SeparatorText("Global Flags:");
        
        ImGui.TextDisabled($"DMC Active:"); ImGui.SameLine();
        if (DMCActive) ImGui.Text("True"); else ImGui.TextDisabled("False");
        
        ImGui.TextDisabled($"DMC Interrupt:"); ImGui.SameLine();
        if (DMCInterrupt) ImGui.Text("True"); else ImGui.TextDisabled("False");
        
        ImGui.TextDisabled($"Frame Interrupt:"); ImGui.SameLine();
        if (FrameInterrupt) ImGui.Text("True"); else ImGui.TextDisabled("False");
        
        ImGui.SeparatorText("Cannels");

        if (ImGui.BeginTable("APUChannels", 7, ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Channel");
            ImGui.TableSetupColumn("Enabled");
            ImGui.TableSetupColumn("Duty/Mode");
            ImGui.TableSetupColumn("Timer");
            ImGui.TableSetupColumn("TimerCounter");
            ImGui.TableSetupColumn("Volume/Linear");
            ImGui.TableSetupColumn("Length");

            ImGui.TableHeadersRow();

            void DrawPulse(string name, PulseChannel p)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(name);
                ImGui.TableNextColumn(); ImGui.Text(p.Enabled.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.Duty.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.Timer.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.TimerCounter.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.Volume.ToString());
                ImGui.TableNextColumn(); ImGui.Text(p.LengthCounter.ToString());
            }

            DrawPulse("Pulse 1", Pulse1);
            DrawPulse("Pulse 2", Pulse2);

            // Triangle
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text("Triangle");
            ImGui.TableNextColumn(); ImGui.Text(Triangle.Enabled.ToString());
            ImGui.TableNextColumn(); ImGui.Text("-");
            ImGui.TableNextColumn(); ImGui.Text(Triangle.Timer.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Triangle.TimerCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Triangle.LinearCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Triangle.LengthCounter.ToString());

            // Noise
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text("Noise");
            ImGui.TableNextColumn(); ImGui.Text(Noise.Enabled.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.Mode.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.Timer.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.TimerCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.Volume.ToString());
            ImGui.TableNextColumn(); ImGui.Text(Noise.LengthCounter.ToString());

            // DMC
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text("DMC");
            ImGui.TableNextColumn(); ImGui.Text(DMC.Enabled.ToString());
            ImGui.TableNextColumn(); ImGui.Text("-");
            ImGui.TableNextColumn(); ImGui.Text(DMC.Timer.ToString());
            ImGui.TableNextColumn(); ImGui.Text(DMC.TimerCounter.ToString());
            ImGui.TableNextColumn(); ImGui.Text(DMC.OutputLevel.ToString());
            ImGui.TableNextColumn(); ImGui.Text(DMC.SampleData?.Length.ToString() ?? "null");

            ImGui.EndTable();
        }

        ImGui.End();
    }
    
    private struct PulseChannel()
    {
        public bool Enabled;
        
        public int Duty;
        
        public int Timer;
        public int TimerCounter;
        
        public int DutyStep;
        
        public int Volume;
        public bool ConstantVolume;
        public int EnvelopeCounter;
        public bool EnvelopeStartFlag;
        
        public int LengthCounter;
        
        public float GetSample()
        {
            if (!Enabled || LengthCounter == 0) return 0f;

            var dutyPattern = new[] {0b00000001, 0b00000011, 0b00001111, 0b11111100}[Duty];
            var output = ((dutyPattern & (1 << DutyStep)) != 0 ? 1f : -1f) * Volume / 15f;
            return output;
        }

        public void Process()
        {
            if (!Enabled) return;
            
            TimerCounter--;
            if (TimerCounter <= 0)
            {
                TimerCounter = Timer;
                DutyStep = (DutyStep + 1) % 8; // ciclo de 8 passos
            }
            
            if (EnvelopeStartFlag)
            {
                EnvelopeCounter = 15;
                EnvelopeStartFlag = false;
            }
            else if (EnvelopeCounter > 0) EnvelopeCounter--;
            if (LengthCounter > 0 && !ConstantVolume) LengthCounter--;
        }
    }
    
    private struct TriangleChannel()
    {
        public bool Enabled;
        
        public int Timer;
        public int TimerCounter;
        
        public int SequenceStep;
        
        public int LinearCounter;
        public bool LinearCounterReload;

        public int LengthCounter;

        public float GetSample()
        {
            if (!Enabled || LengthCounter == 0 || LinearCounter == 0) return 0f;
            
            var value = SequenceStep < 16 ? SequenceStep : 31 - SequenceStep;
            return (value / 15f) * 2f - 1f;
        }
    }
    
    private struct NoiseChannel()
    {
        public bool Enabled;
        
        private ushort Lfsr = 1;
        
        public int Timer;
        public int TimerCounter;
        
        public int Volume;
        public bool ConstantVolume;
        public int EnvelopeCounter;
        public bool EnvelopeStartFlag;
        
        public int LengthCounter;
        
        public bool Mode;

        public float GetSample()
        {
            if (!Enabled || LengthCounter == 0) return 0f;
            
            int bit = (Lfsr & 1) ^ ((Mode ? (Lfsr >> 6) : (Lfsr >> 1)) & 1);
            Lfsr = (ushort)((Lfsr >> 1) | (bit << 14));

            return ((Lfsr & 1) != 0 ? 1f : -1f) * Volume / 15f;
        }
    }
    
    private struct DMCChannel()
    {
        public bool Enabled;
        
        public byte[] SampleData;
        public int SampleIndex;
        
        public int Timer;
        public int TimerCounter;
        
        public int OutputLevel;

        public float GetSample()
        {
            if (!Enabled || SampleData == null || SampleIndex >= SampleData.Length) return 0f;
            return ((SampleData[SampleIndex] / 127f) * 2f - 1f);
        }
    }

}
