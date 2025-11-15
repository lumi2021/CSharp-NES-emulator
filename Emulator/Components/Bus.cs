using Emulator.Components.Core;

namespace Emulator.Components;

public class Bus(VirtualSystem sys): Component(sys)
{

    private byte _cpuLast = 0;
    private byte _ppuLast = 0;
    
    public byte CpuRead(ushort address)
    {
        switch (address)
        {
            case < 0x2000:
                _cpuLast = sys.Ram.Read(address);
                break;
            
            case < 0x4000 or 0x4014:
                _cpuLast = sys.Ppu.ReadRegister(address);
                break;
            
            case 0x4015:
                _cpuLast = sys.Apu.ReadStatus();
                break;
            
            case 0x4016:
                _cpuLast = sys.Joy1.InputBitRegister;
                break;
            
            case 0x4017:
                _cpuLast = 0; //sys.Joy2.InputBitRegister,
                break;
            
            case >= 0x4020:
                _cpuLast = sys.RomMapper.CpuRead(address);
                break;
            
            default:
                Console.WriteLine($"Invalid read address {address:x4}");
                break;
        }

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
            case 0x4014: sys.Ppu.WriteRegister(address, value); break;
            case 0x4015: system.Apu.Write(address, value); break;
            
            case 0x4016:
                sys.Joy1.Mode = value == 0 ? JoyControllerMode.Read : JoyControllerMode.Write;
                //sys.Joy2.Mode = value == 0 ? JoyControllerMode.Read : JoyControllerMode.Write;
                break;
            
            case 0x4017:
                sys.Apu.Write(address, value);
                break;
            
            case >= 0x4020: sys.RomMapper.CpuWrite(address, value); break;
            
            default:
                Console.WriteLine($"Invalid write address {address:x4}");
                break;
        }
    }

    public byte PpuRead(ushort address) => sys.RomMapper.PpuRead(address);
    public void PpuWrite(ushort address, byte value) => sys.RomMapper.PpuWrite(address, value);
}
