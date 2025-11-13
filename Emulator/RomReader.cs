using Emulator.Mappers;

namespace Emulator;

internal static class RomReader
{

    public static NesRom LoadFromPath(string path)
    {
        byte[] data = File.ReadAllBytes(path);

        if (data[0..4] is [0x4E, 0x45, 0x53, 0x1A]) return new NesRom(data);
        throw new Exception("Invalid NES room!");
    }

}

public class NesRom
{

    private byte[] header = [];
    private byte[] trainer = [];
    private byte[] prgData = [];
    private byte[] chrData = [];

    public Mapper mapper;

    // Header data
    public byte PRGDataSize16KB => header[4];
    public byte CHRDataSize8KB => header[5];

    public bool Trainer => ((header[6] >> 6) & 1) == 1;
    public NametableArrangement NametableArrangement => ((header[6] >> 8) & 1) == 0
        ? NametableArrangement.Horizontal
        : NametableArrangement.Vertical;

    public byte[] PrgData => [.. prgData];
    public byte[] ChrData => [.. chrData];

    public NesRom(byte[] data)
    {
        header = data[0..16];

        int b = 16;
        if (Trainer)
        {
            trainer = data[b..(b+512)];
            b = 528;
        }

        int dl = PRGDataSize16KB * 16 * 1024;
        prgData = data[b .. (b + dl)];
        b += dl;

        dl = CHRDataSize8KB * 8 * 1024;
        chrData = data[b..(b + dl)];
        b += dl;

        mapper = GetMapper((byte)((header[6] >> 4) | (header[7] & 0xF0)), this);
    }

    private static Mapper GetMapper(byte mapper, NesRom parent)
    {
        return mapper switch
        {
            0x00 => new NROM(parent),

            _ => throw new NotImplementedException($"mapper {mapper}")
        };
    }
}

public enum NametableArrangement : byte
{
    Horizontal,
    Vertical,
}
