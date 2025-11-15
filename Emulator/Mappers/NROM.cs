using Emulator.Components;

namespace Emulator.Mappers;

internal class NROM(NesRom rom) : Mapper(rom)
{

    private readonly byte[] _prg = rom.PrgData;
    private readonly byte[] _chr = rom.ChrData;
    private readonly bool _hasTwoPrgBanks = rom.PRGDataSize16KB > 1;

    public override byte CpuRead(ushort address)
    {
        return address switch
        {
            >= 0x8000 and < 0xC000 => _prg[address - 0x8000],
            >= 0xC000 => _hasTwoPrgBanks
                ? _prg[address - 0x8000]
                : _prg[address - 0xC000],
            
            _ => 0
        };
    }

    public override void CpuWrite(ushort address, byte value)
    {
        throw new NotImplementedException();
    }

    public override byte PpuRead(ushort address)
    {
        return address switch
        {
            <= 0x1fff => _chr[address],
            _ => throw new ArgumentOutOfRangeException(nameof(address), address, null)
        };
    }

    public override void PpuWrite(ushort address, byte value)
    {
        throw new NotImplementedException();
    }
}
