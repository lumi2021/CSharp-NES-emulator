using System.Diagnostics;
using Emulator.Components.Storage;
using Emulator.Mappers;

namespace Emulator.Components;

public class VirtualSystem
{
    private Bus _bus;
    private Cpu _cpu;
    private Ppu _ppu;
    private Apu _apu;
    private RamMemory _ramMemory;
    private RomMemory _romMemory;

    private JoyController _joy1;
    //private JoyController _joy2;

    public Bus Bus => _bus;
    public Cpu Cpu => _cpu;
    public Ppu Ppu => _ppu;
    public Apu Apu => _apu;
    public RamMemory Ram => _ramMemory;
    public RomMemory Rom => _romMemory;

    public JoyController Joy1 => _joy1;

    public Mapper RomMapper => _romMemory.RomData.mapper;

    public VirtualSystem()
    {
        _bus = new(this);
        _cpu = new(this);
        _ppu = new(this);
        _apu = new(this);
        _ramMemory = new(this);
        _romMemory = new(this);

        _joy1 = new(0, this, Program.input);
    }


    public void Process(double delta)
    {
        var todoClocks = Math.Min(8000, delta * 1_789_773.0);
        
        var restingClocks = (int)(todoClocks * 0.92);
        while ((!_cpu.paused || _cpu.doStep) && restingClocks > 0)
        {
            _cpu.Tick();
            _apu.Tick();
            
            restingClocks -= _cpu.clockCount;
        }
        
        _ppu.OnVblank = true;
        _ppu.Tick();
        
        restingClocks = (int)(todoClocks * 0.08);
        while ((!_cpu.paused || _cpu.doStep) && restingClocks > 0)
        {
            _cpu.Tick();
            _apu.Tick();
            
            restingClocks -= _cpu.clockCount;
        }
        
        _ppu.OnVblank = false;
    }

    public void Draw()
    {
        
    }
    
    public void InsertCartriadge(NesRom rom)
    {
        Console.WriteLine($"Mapper: {rom.mapper.GetType().Name}");
        Console.WriteLine($"PRG size: {rom.PRGDataSize16KB * 16} KiB ({rom.PRGDataSize16KB})");
        Console.WriteLine($"CHR size: {rom.CHRDataSize8KB * 8} KiB ({rom.CHRDataSize8KB})");
        Console.WriteLine($"Nametable arrangementg: {rom.NametableArrangement}");

        _romMemory.LoadRom(rom);

        _cpu.Reset();
        _ppu.ResetRomData();

        Console.WriteLine($"Entry: ${_cpu.ResetVector:X4}");
    }
    

}

