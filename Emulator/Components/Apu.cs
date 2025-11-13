using Emulator.Components.Core;

namespace Emulator.Components;

public class Apu : Component
{

    public bool DMCEnabled = false;
    public bool DMCActive = false;

    public bool DMCInterrupt = false;
    public bool FrameInterrupt = false;

    // Pulse 1 Channel
    public bool Pulse1Enabled = false;
    public int Pulse1Counter = 0;

    // Pulse 2 Channel
    public bool Pulse2Enabled = false;
    public int Pulse2Counter = 0;

    // Noise Channel
    public bool NoiseEnabled = false;
    public int NoiseCounter = 0;

    // Triangle 1 Channel
    public bool TriangleEnabled = false;
    public int TriangleCounter = 0;


    public Apu(VirtualSystem sys) : base(sys)
    {
    
    }

    public byte ReadStatus()
    {
        int value = 0;

        value |= 0b_10000000 * (DMCInterrupt ? 1 : 0);
        value |= 0b_01000000 * (FrameInterrupt ? 1 : 0);
        value |= 0b_00010000 * (DMCActive ? 1 : 0);

        value |= 0b_00001000 * (NoiseCounter > 0 ? 1 : 0);
        value |= 0b_00000100 * (TriangleCounter > 0 ? 1 : 0);
        value |= 0b_00000010 * (Pulse2Counter > 0 ? 1 : 0);
        value |= 0b_00000001 * (Pulse1Counter > 0 ? 1 : 0);

        return (byte)value;
    }
    public void Write(ushort addr, byte value)
    {

    }

}
