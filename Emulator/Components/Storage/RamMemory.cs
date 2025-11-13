using Emulator.Components.Core;
using ImGuiNET;

namespace Emulator.Components.Storage;

public class RamMemory : Component
{

    byte[] _cpu_ram = new byte[0x2000];
    byte[] _prg_rom = new byte[0xBFE0];

    (ushort addrStart, ushort addrEnd, string label)[] _mem_labels = [
            (0x2000, 0x2FFF, "Unused (PPU regs)"),
            (0x8000, 0xFFFF, "PRG"),
        ];
    public int viewingPage = 0;

    public RamMemory(VirtualSystem sys) : base(sys)
    {
        Program.DrawPopup += ShowMemory;
    }

    private void ShowMemory()
    {
        ImGui.Begin("Memory View", ImGuiWindowFlags.AlwaysAutoResize);
        {

            ImGui.SeparatorText("Zero Page:");
            for (int y = 0x0; y < 0xFF; y += 16)
            {
                ImGui.TextDisabled($"${y:X4}"); ImGui.SameLine();
                var data = _cpu_ram[y..(y + 16)].Select(e => $"{e:X2}");
                ImGui.Text(string.Join(' ', data));

                var labels = _mem_labels
                    .Where(e => y >= e.addrStart && y + 15 <= e.addrEnd)
                    .Select(e => e.label)
                    .ToArray();
                if (labels.Length > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(string.Join("; ", labels));
                }
            }

            ImGui.SeparatorText("Stack Page:");
            for (int y = 0x100; y < 0x1FF; y += 16)
            {
                ImGui.TextDisabled($"${y:X4}"); ImGui.SameLine();
                var data = _cpu_ram[y..(y + 16)].Select(e => $"{e:X2}");
                ImGui.Text(string.Join(' ', data));

                var labels = _mem_labels
                    .Where(e => y >= e.addrStart && y + 15 <= e.addrEnd)
                    .Select(e => e.label)
                    .ToArray();
                if (labels.Length > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(string.Join("; ", labels));
                }
            }

        }
        ImGui.End();
    }

    public void Write(ushort addr, params byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            ushort gaddr = GetGlobalAddr((ushort)(addr + i));

            if (gaddr < 0x8000)
                _cpu_ram[gaddr + i] = data[i];

            else
                _prg_rom[gaddr - 0x4020] = data[i];
        }
    }
    public byte Read(int addr) => Read((ushort)addr);
    public byte Read(ushort addr)
    {
        ushort gaddr = GetGlobalAddr(addr);

        if (gaddr < 0x8000)
            return _cpu_ram[gaddr];

        else
            return _prg_rom[gaddr - 0x4020];
    }

    public byte[] CopyPage(byte page)
    {
        ushort addr = (ushort)(page << 8);
        return _cpu_ram[addr..(addr + 256)];
    }

    private ushort GetGlobalAddr(ushort addr)
    {
        ushort gaddr = addr;

        if (addr < 0x2000)
            gaddr = (ushort)(addr & 0x07FF);

        return gaddr;
    }
}
