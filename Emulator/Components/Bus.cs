using Emulator.Components.Core;

namespace Emulator.Components;

public class Bus(VirtualSystem sys): Component(sys)
{

    private byte _cpuLast = 0;
    private byte _ppuLast = 0;
    
    public byte CpuRead(ushort address)
    {
        _cpuLast = address switch
        {
            < 0x2000 => sys.Ram.Read(address),
            >= 0x2000 and < 0x4000 => sys.Ppu.ReadRegister(address),
            
            0x4015 => sys.Apu.ReadStatus(),
            
            0x4016 => sys.Joy1.InputBitRegister,
            0x4017 => 0, //sys.Joy2.InputBitRegister,
            
            >= 0x4020 => sys.RomMapper.CpuRead(system, address),
            
            _ => _cpuLast,
        };

        return _cpuLast;
    }
    public void CpuWrite(ushort address, byte value)
    {
        _cpuLast = value;
        
        switch (address)
        {
            case < 0x2000: system.Ram.Write(address, value); break;
            case < 0x4000: sys.Ppu.WriteRegister(address, value); break;
            
            case <= 0x4013: system.Apu.Write(address, value); break;
            case 0x4015: system.Apu.Write(address, value); break;
            
            case 0x4016:
                sys.Joy1.Mode = value == 0 ? JoyControllerMode.Read : JoyControllerMode.Write;
                //sys.Joy2.Mode = value == 0 ? JoyControllerMode.Read : JoyControllerMode.Write;
                break;
            
            case >= 0x4020: sys.RomMapper.CpuWrite(system, address, value); break;
        }
    }

    public byte PpuRead(ushort address) => sys.RomMapper.PpuRead(system, address);
    public void PpuWrite(ushort address, byte value) => sys.RomMapper.PpuWrite(system, address, value);
}
