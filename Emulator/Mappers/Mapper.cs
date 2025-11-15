using Emulator.Components;

namespace Emulator.Mappers;

public abstract class Mapper(NesRom rom)
{
    
    public abstract byte CpuRead(ushort address);
    public abstract void CpuWrite(ushort address, byte value);
    
    public abstract byte PpuRead(ushort address);
    public abstract void PpuWrite(ushort address, byte value);
    

    
}
