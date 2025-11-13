using Emulator.Components;

namespace Emulator.Mappers;

internal class NROM(NesRom r) : Mapper(r)
{


    public override byte CpuRead(VirtualSystem sys, ushort address)
    {
        return address switch
        {
            > 0x8000 and < 0xC000 => sys.Rom.RomData.PrgData[address - 0x8000],
            >= 0xC000 => sys.Rom.RomData.PRGDataSize16KB == 2
                ? sys.Rom.RomData.PrgData[address - 0x8000]
                : sys.Rom.RomData.PrgData[address - 0xC000],
            
            _ => 0
        };
    }

    public override void CpuWrite(VirtualSystem sys, ushort address, byte value)
    {
        throw new NotImplementedException();
    }

    public override byte PpuRead(VirtualSystem sys, ushort address)
    {
        return address switch
        {
            <= 0x1fff => sys.Rom.RomData.ChrData[address],
            _ => throw new ArgumentOutOfRangeException(nameof(address), address, null)
        };
    }

    public override void PpuWrite(VirtualSystem sys, ushort address, byte value)
    {
        throw new NotImplementedException();
    }
}
