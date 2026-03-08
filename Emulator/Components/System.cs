using System.Diagnostics;
using Emulator.Components.Storage;
using Emulator.Mappers;

namespace Emulator.Components;

public class VirtualSystem
{
    private const double NES_FPS = 60.0988;
    private const double FRAME_TIME = 1.0 / NES_FPS;
    private double frameAccumulator;
    
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
        if (_cpu.paused)
        {
            if (_cpu.doStep) _cpu.doStep = false;
            else return;
        }
     
        frameAccumulator += delta;
        while (frameAccumulator >= FRAME_TIME)
        {
            const int SCANLINES_PER_FRAME = 262;
            const int CPU_CYCLES_PER_SCANLINE = 114;

            var scanlines = 0;
            var cycles = 0;

            while (scanlines < SCANLINES_PER_FRAME)
            {
                while (cycles < CPU_CYCLES_PER_SCANLINE)
                {
                    _cpu.Step();
                    _apu.Step(_cpu.clockCount);
                    cycles += _cpu.clockCount;
                }

                _ppu.ProcessScanline();
                cycles = 0;
                scanlines++;
            }
            
            frameAccumulator -= FRAME_TIME;
        }
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

