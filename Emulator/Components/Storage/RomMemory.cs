using Emulator.Components.Core;

namespace Emulator.Components.Storage;

public class RomMemory(VirtualSystem sys) : Component(sys)
{

    private NesRom? _rom;
    public NesRom RomData => _rom!;
    public bool HasRom => _rom != null;
    
    public void LoadRom(NesRom rom) => _rom = rom;
    
}
