using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RokMahjong.Core;

namespace RokMahjong.Game;

public sealed class TileAtlas : IDisposable
{
    private readonly Texture2D _texture;
    private readonly TileAtlasMetadata _metadata;
    private readonly Dictionary<string, int> _indices;

    private TileAtlas(Texture2D texture, TileAtlasMetadata metadata)
    {
        _texture = texture;
        _metadata = metadata;
        _indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < metadata.FaceOrder.Length; i++)
            _indices[metadata.FaceOrder[i]] = i;
    }

    public Texture2D Texture => _texture;
    public int CellWidth => _metadata.CellWidth;
    public int CellHeight => _metadata.CellHeight;
    public int FaceLeft => _metadata.FaceLeft;
    public int FaceTop => _metadata.FaceTop;
    public int FaceWidth => _metadata.FaceWidth;
    public int FaceHeight => _metadata.FaceHeight;

    public static TileAtlas Load(GraphicsDevice device, Assembly assembly)
    {
        using var png = assembly.GetManifestResourceStream("RokMahjong.Game.tile-atlas.png")
            ?? throw new InvalidOperationException("Embedded tile atlas PNG is missing.");
        using var json = assembly.GetManifestResourceStream("RokMahjong.Game.tile-atlas.json")
            ?? throw new InvalidOperationException("Embedded tile atlas metadata is missing.");
        var metadata = JsonSerializer.Deserialize<TileAtlasMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Tile atlas metadata could not be read.");
        var texture = Texture2D.FromStream(device, png, DefaultColorProcessors.PremultiplyAlpha);
        return new TileAtlas(texture, metadata);
    }

    public bool TryGetSourceRect(TileFace face, out Rectangle source)
    {
        if (_indices.TryGetValue(face.Label, out var index))
        {
            var column = index % _metadata.Columns;
            var row = index / _metadata.Columns;
            source = new Rectangle(
                column * _metadata.CellWidth,
                row * _metadata.CellHeight,
                _metadata.CellWidth,
                _metadata.CellHeight);
            return true;
        }

        source = default;
        return false;
    }

    public Rectangle GetDestinationRect(Rectangle faceBounds)
    {
        var scale = faceBounds.Width / (float)_metadata.FaceWidth;
        var width = Math.Max(1, (int)MathF.Round(_metadata.CellWidth * scale));
        var height = Math.Max(1, (int)MathF.Round(_metadata.CellHeight * scale));
        var x = faceBounds.X - (int)MathF.Round(_metadata.FaceLeft * scale);
        var y = faceBounds.Y - (int)MathF.Round(_metadata.FaceTop * scale);
        return new Rectangle(x, y, width, height);
    }

    public void Dispose() => _texture.Dispose();
}

public sealed class TileAtlasMetadata
{
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
    public int FaceLeft { get; set; }
    public int FaceTop { get; set; }
    public int FaceWidth { get; set; }
    public int FaceHeight { get; set; }
    public int FaceCount { get; set; }
    public string[] FaceOrder { get; set; } = Array.Empty<string>();
}
