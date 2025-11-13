using Emulator.Components;

namespace Emulator.Mappers;

public abstract class Mapper(NesRom rom)
{
    public readonly NesRom romReference = rom;

    public abstract byte CpuRead(VirtualSystem sys, ushort address);
    public abstract void CpuWrite(VirtualSystem sys, ushort address, byte value);
    
    public abstract byte PpuRead(VirtualSystem sys, ushort address);
    public abstract void PpuWrite(VirtualSystem sys, ushort address, byte value);
    

    
}
