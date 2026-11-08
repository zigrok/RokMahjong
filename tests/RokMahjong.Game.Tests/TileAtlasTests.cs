using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using RokMahjong.Core;
using RokMahjong.Game;

namespace RokMahjong.Game.Tests;

public class TileAtlasTests
{
    private static readonly Assembly Assembly = typeof(MahjongBoardView).Assembly;

    private static TileAtlasMetadata ReadMetadata()
    {
        using var json = Assembly.GetManifestResourceStream("RokMahjong.Game.tile-atlas.json")
            ?? throw new InvalidOperationException("Embedded tile atlas metadata is missing.");
        var metadata = JsonSerializer.Deserialize<TileAtlasMetadata>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Embedded tile atlas metadata is unreadable.");
        return metadata;
    }

    [Fact]
    public void Embedded_atlas_covers_every_tile_face()
    {
        var metadata = ReadMetadata();

        Assert.Equal(42, metadata.FaceCount);
        Assert.Equal(42, metadata.FaceOrder.Length);
        Assert.Equal(metadata.FaceOrder.Length, metadata.FaceOrder.Distinct().Count());
        foreach (var face in Layouts.Faces)
            Assert.Contains(face.Label, metadata.FaceOrder);
    }

    [Fact]
    public void Embedded_atlas_image_matches_its_metadata()
    {
        var metadata = ReadMetadata();
        using var png = Assembly.GetManifestResourceStream("RokMahjong.Game.tile-atlas.png")
            ?? throw new InvalidOperationException("Embedded tile atlas image is missing.");

        Span<byte> header = stackalloc byte[24];
        png.ReadExactly(header);
        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);

        Assert.Equal(metadata.Columns * metadata.CellWidth, width);
        Assert.Equal(metadata.Rows * metadata.CellHeight, height);
        Assert.True(metadata.FaceWidth > 0 && metadata.FaceHeight > 0);
        Assert.True(metadata.FaceLeft + metadata.FaceWidth <= metadata.CellWidth);
        Assert.True(metadata.FaceTop + metadata.FaceHeight <= metadata.CellHeight);
    }
}
