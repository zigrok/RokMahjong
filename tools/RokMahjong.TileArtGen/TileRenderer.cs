using System.Numerics;

namespace RokMahjong.TileArtGen;

/// <summary>
/// A glyph texture applied to the flat top face of the 3D tile.
/// </summary>
/// <param name="Width">Texture width in pixels.</param>
/// <param name="Height">Texture height in pixels.</param>
/// <param name="Rgba">Non-premultiplied RGBA pixels, row-major.</param>
/// <param name="HasAlpha">True when the texture is a transparent glyph layer.</param>
/// <param name="HeightMap">Optional single-channel height map, same size as the texture.</param>
public sealed record GlyphTexture(int Width, int Height, byte[] Rgba, bool HasAlpha, byte[]? HeightMap);

/// <summary>
/// Software raymarcher for the mahjong tile body.
/// </summary>
/// <remarks>
/// The tile is a rounded box rendered with a perspective camera. The flat top
/// face carries the glyph albedo and a height field used for bump shading, so
/// the carved glyph picks up real highlights and ambient occlusion. The result
/// is deterministic and needs no GPU, which keeps the atlas reproducible.
/// </remarks>
public static class TileRenderer
{
    private const float TileWidth = 1.0f;
    private const float TileDepth = 0.55f;
    private const float EdgeRadius = 0.065f;
    private const float HeightScale = 0.006f;
    private const float FieldOfViewDegrees = 22f;

    public static byte[] RenderCell(
        int cellWidth,
        int cellHeight,
        int faceLeft,
        int faceTop,
        int faceWidth,
        int faceHeight,
        GlyphTexture glyph,
        float yawDegrees = -16f,
        float pitchDegrees = 10f,
        int supersample = 2)
    {
        var tileHeight = TileWidth * faceHeight / faceWidth;
        var rotation = Matrix4x4.CreateRotationY(DegreesToRadians(yawDegrees))
                       * Matrix4x4.CreateRotationX(DegreesToRadians(pitchDegrees));
        var inverseRotation = Matrix4x4.Transpose(rotation);

        var radius = MathF.Sqrt(
            TileWidth * TileWidth / 4f + tileHeight * tileHeight / 4f + TileDepth * TileDepth / 4f);
        var distance = radius / MathF.Sin(DegreesToRadians(FieldOfViewDegrees / 2f)) * 1.25f;
        var eye = new Vector3(0, 0, distance);
        var tanHalf = MathF.Tan(DegreesToRadians(FieldOfViewDegrees / 2f));

        var renderWidth = cellWidth * supersample;
        var renderHeight = cellHeight * supersample;
        var aspect = renderWidth / (float)renderHeight;
        var frame = new Vector4[renderWidth * renderHeight];

        var halfExtents = new Vector3(TileWidth / 2f, tileHeight / 2f, TileDepth / 2f);
        var blurredHeight = BuildHeightField(glyph);

        Parallel.For(0, renderHeight, y =>
        {
            for (var x = 0; x < renderWidth; x++)
            {
                var ndcX = (x + 0.5f) / renderWidth * 2f - 1f;
                var ndcY = 1f - (y + 0.5f) / renderHeight * 2f;
                var direction = Vector3.Normalize(new Vector3(
                    ndcX * tanHalf * aspect,
                    ndcY * tanHalf,
                    -1f));

                var localOrigin = Vector3.Transform(eye, inverseRotation);
                var localDirection = Vector3.TransformNormal(direction, inverseRotation);

                var color = Trace(
                    localOrigin,
                    localDirection,
                    rotation,
                    inverseRotation,
                    halfExtents,
                    tileHeight,
                    glyph,
                    blurredHeight,
                    out var hit);
                frame[y * renderWidth + x] = hit ? color : Vector4.Zero;
            }
        });

        // Fit the whole tile silhouette, not just the flat face, so the bevel
        // and side stay inside the logical tile bounds instead of being covered
        // by the neighbouring tile on a packed board.
        var corners = ProjectSilhouette(rotation, eye, tanHalf, aspect, halfExtents, renderWidth, renderHeight);
        var minX = corners.Min(c => c.X);
        var minY = corners.Min(c => c.Y);
        var maxX = corners.Max(c => c.X);
        var maxY = corners.Max(c => c.Y);

        var output = new byte[cellWidth * cellHeight * 4];
        var scaleX = faceWidth / (maxX - minX);
        var scaleY = faceHeight / (maxY - minY);
        for (var y = 0; y < cellHeight; y++)
        {
            for (var x = 0; x < cellWidth; x++)
            {
                var sourceX = minX + (x + 0.5f - faceLeft) / scaleX - 0.5f;
                var sourceY = minY + (y + 0.5f - faceTop) / scaleY - 0.5f;
                var color = SampleBilinear(frame, renderWidth, renderHeight, sourceX, sourceY);
                var offset = (y * cellWidth + x) * 4;
                output[offset] = ToByte(color.X * color.W);
                output[offset + 1] = ToByte(color.Y * color.W);
                output[offset + 2] = ToByte(color.Z * color.W);
                output[offset + 3] = ToByte(color.W);
            }
        }

        return output;
    }

    private static Vector4 Trace(
        Vector3 origin,
        Vector3 direction,
        Matrix4x4 rotation,
        Matrix4x4 inverseRotation,
        Vector3 halfExtents,
        float tileHeight,
        GlyphTexture glyph,
        float[] blurredHeight,
        out bool hit)
    {
        var travel = 0f;
        const float maxDistance = 24f;
        for (var step = 0; step < 96; step++)
        {
            var point = origin + direction * travel;
            var distance = SdRoundBox(point, halfExtents, EdgeRadius);
            if (distance < 0.0009f)
            {
                hit = true;
                return Shade(
                    point, direction, rotation, inverseRotation, halfExtents, tileHeight, glyph,
                    blurredHeight);
            }

            travel += distance;
            if (travel > maxDistance) break;
        }

        hit = false;
        return Vector4.Zero;
    }

    private static Vector4 Shade(
        Vector3 localPoint,
        Vector3 worldDirection,
        Matrix4x4 rotation,
        Matrix4x4 inverseRotation,
        Vector3 halfExtents,
        float tileHeight,
        GlyphTexture glyph,
        float[] blurredHeight)
    {
        var localNormal = NormalAt(localPoint, halfExtents);
        var isFace = localNormal.Z > 0.72f && localPoint.Z > 0f;

        var u = (localPoint.X + TileWidth / 2f) / TileWidth;
        var v = (TileHeightFrom(halfExtents) / 2f - localPoint.Y) / TileHeightFrom(halfExtents);
        u = Math.Clamp(u, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);

        var albedo = isFace
            ? new Vector3(0.96f, 0.93f, 0.86f)
            : new Vector3(0.80f, 0.72f, 0.60f);
        var height = 0.5f;
        var gradient = 0f;

        if (isFace)
        {
            var texel = SampleGlyph(glyph, u, v);
            var alpha = texel.W;
            if (glyph.HasAlpha)
            {
                albedo = Vector3.Lerp(albedo, new Vector3(texel.X, texel.Y, texel.Z), alpha);
            }
            else
            {
                albedo = new Vector3(texel.X, texel.Y, texel.Z);
            }

            height = SampleHeight(glyph, blurredHeight, u, v);
            var step = 1.5f / Math.Max(glyph.Width, glyph.Height);
            var left = SampleHeight(glyph, blurredHeight, Math.Clamp(u - step, 0f, 1f), v);
            var right = SampleHeight(glyph, blurredHeight, Math.Clamp(u + step, 0f, 1f), v);
            var up = SampleHeight(glyph, blurredHeight, u, Math.Clamp(v - step, 0f, 1f));
            var down = SampleHeight(glyph, blurredHeight, u, Math.Clamp(v + step, 0f, 1f));
            var dhdu = (right - left) / (2f * step);
            var dhdv = (down - up) / (2f * step);
            gradient = MathF.Sqrt(dhdu * dhdu + dhdv * dhdv);

            var bump = new Vector3(
                -HeightScale * dhdu / TileWidth,
                HeightScale * dhdv / TileHeightFrom(halfExtents),
                1f);
            localNormal = Vector3.Normalize(Vector3.Lerp(localNormal, Vector3.Normalize(bump), 0.85f));
        }

        var normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, rotation));
        var view = Vector3.Normalize(-worldDirection);

        var key = Vector3.Normalize(new Vector3(-0.45f, 0.58f, 0.68f));
        var fill = Vector3.Normalize(new Vector3(0.55f, 0.10f, 0.62f));

        var keyDiffuse = MathF.Max(Vector3.Dot(normal, key), 0f);
        var fillDiffuse = MathF.Max(Vector3.Dot(normal, fill), 0f);
        var half = Vector3.Normalize(key + view);
        var specular = MathF.Pow(MathF.Max(Vector3.Dot(normal, half), 0f), 42f) * 0.45f;

        var ambientOcclusion = AmbientOcclusion(localPoint, halfExtents, localNormal);
        var crease = isFace ? Math.Clamp(1f - gradient * 0.015f, 0.85f, 1f) : 1f;
        var relief = isFace ? 1f + (height - 0.5f) * 0.04f : 1f;

        var light = 0.24f * ambientOcclusion + keyDiffuse * 0.88f + fillDiffuse * 0.10f;
        var color = albedo * light * crease * relief + new Vector3(specular);

        // Slight warm bounce from the ivory body onto the glyph.
        color += new Vector3(0.02f, 0.018f, 0.012f) * ambientOcclusion;

        return new Vector4(
            Math.Clamp(color.X, 0f, 1f),
            Math.Clamp(color.Y, 0f, 1f),
            Math.Clamp(color.Z, 0f, 1f),
            1f);
    }

    private static float AmbientOcclusion(Vector3 point, Vector3 halfExtents, Vector3 normal)
    {
        var occlusion = 0f;
        var weight = 1f;
        for (var i = 1; i <= 5; i++)
        {
            var distance = 0.015f + 0.06f * i;
            var samplePoint = point + normal * distance;
            var field = SdRoundBox(samplePoint, halfExtents, EdgeRadius);
            occlusion += (distance - field) * weight;
            weight *= 0.7f;
        }

        return Math.Clamp(1f - 1.35f * occlusion, 0.25f, 1f);
    }

    private static float SdRoundBox(Vector3 point, Vector3 halfExtents, float radius)
    {
        var q = new Vector3(
            MathF.Abs(point.X),
            MathF.Abs(point.Y),
            MathF.Abs(point.Z)) - halfExtents + new Vector3(radius);
        var outside = new Vector3(MathF.Max(q.X, 0f), MathF.Max(q.Y, 0f), MathF.Max(q.Z, 0f));
        return outside.Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f) - radius;
    }

    private static Vector3 NormalAt(Vector3 point, Vector3 halfExtents)
    {
        const float epsilon = 0.0015f;
        var dx = SdRoundBox(point + new Vector3(epsilon, 0, 0), halfExtents, EdgeRadius)
                 - SdRoundBox(point - new Vector3(epsilon, 0, 0), halfExtents, EdgeRadius);
        var dy = SdRoundBox(point + new Vector3(0, epsilon, 0), halfExtents, EdgeRadius)
                 - SdRoundBox(point - new Vector3(0, epsilon, 0), halfExtents, EdgeRadius);
        var dz = SdRoundBox(point + new Vector3(0, 0, epsilon), halfExtents, EdgeRadius)
                 - SdRoundBox(point - new Vector3(0, 0, epsilon), halfExtents, EdgeRadius);
        return Vector3.Normalize(new Vector3(dx, dy, dz));
    }

    private static float TileHeightFrom(Vector3 halfExtents) => halfExtents.Y * 2f;

    private static Vector4 SampleGlyph(GlyphTexture glyph, float u, float v)
    {
        var x = Math.Clamp((int)(u * (glyph.Width - 1)), 0, glyph.Width - 1);
        var y = Math.Clamp((int)(v * (glyph.Height - 1)), 0, glyph.Height - 1);
        var offset = (y * glyph.Width + x) * 4;
        return new Vector4(
            glyph.Rgba[offset] / 255f,
            glyph.Rgba[offset + 1] / 255f,
            glyph.Rgba[offset + 2] / 255f,
            glyph.Rgba[offset + 3] / 255f);
    }

    private static float SampleHeight(GlyphTexture glyph, float[] blurred, float u, float v)
    {
        if (glyph.HeightMap is not null)
        {
            var x = Math.Clamp((int)(u * (glyph.Width - 1)), 0, glyph.Width - 1);
            var y = Math.Clamp((int)(v * (glyph.Height - 1)), 0, glyph.Height - 1);
            return glyph.HeightMap[y * glyph.Width + x] / 255f;
        }

        var bx = Math.Clamp((int)(u * (glyph.Width - 1)), 0, glyph.Width - 1);
        var by = Math.Clamp((int)(v * (glyph.Height - 1)), 0, glyph.Height - 1);
        return blurred[by * glyph.Width + bx];
    }

    private static float[] BuildHeightField(GlyphTexture glyph)
    {
        var field = new float[glyph.Width * glyph.Height];
        if (glyph.HeightMap is not null)
        {
            for (var i = 0; i < field.Length; i++) field[i] = glyph.HeightMap[i] / 255f;
            return field;
        }

        if (!glyph.HasAlpha)
        {
            Array.Fill(field, 0.5f);
            return field;
        }

        for (var i = 0; i < field.Length; i++) field[i] = glyph.Rgba[i * 4 + 3] / 255f;
        return Blur(field, glyph.Width, glyph.Height, 4);
    }

    private static float[] Blur(float[] source, int width, int height, int radius)
    {
        var horizontal = new float[source.Length];
        var output = new float[source.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                var count = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sx = Math.Clamp(x + k, 0, width - 1);
                    sum += source[y * width + sx];
                    count++;
                }

                horizontal[y * width + x] = sum / count;
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                var count = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sy = Math.Clamp(y + k, 0, height - 1);
                    sum += horizontal[sy * width + x];
                    count++;
                }

                output[y * width + x] = sum / count;
            }
        }

        return output;
    }

    private static Vector2[] ProjectSilhouette(
        Matrix4x4 rotation,
        Vector3 eye,
        float tanHalf,
        float aspect,
        Vector3 halfExtents,
        int width,
        int height)
    {
        var corners = new[]
        {
            new Vector3(-halfExtents.X, -halfExtents.Y, -halfExtents.Z),
            new Vector3(halfExtents.X, -halfExtents.Y, -halfExtents.Z),
            new Vector3(halfExtents.X, halfExtents.Y, -halfExtents.Z),
            new Vector3(-halfExtents.X, halfExtents.Y, -halfExtents.Z),
            new Vector3(-halfExtents.X, -halfExtents.Y, halfExtents.Z),
            new Vector3(halfExtents.X, -halfExtents.Y, halfExtents.Z),
            new Vector3(halfExtents.X, halfExtents.Y, halfExtents.Z),
            new Vector3(-halfExtents.X, halfExtents.Y, halfExtents.Z),
        };

        var projected = new Vector2[8];
        for (var i = 0; i < corners.Length; i++)
        {
            var world = Vector3.Transform(corners[i], rotation);
            var local = world - eye;
            var depth = -local.Z;
            var ndcX = local.X / (depth * tanHalf * aspect);
            var ndcY = local.Y / (depth * tanHalf);
            projected[i] = new Vector2(
                (ndcX + 1f) / 2f * width,
                (1f - ndcY) / 2f * height);
        }

        return projected;
    }

    private static Vector4 SampleBilinear(Vector4[] frame, int width, int height, float x, float y)
    {
        if (x < -1f || y < -1f || x > width || y > height) return Vector4.Zero;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;

        var result = Vector4.Zero;
        for (var dy = 0; dy <= 1; dy++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                var sx = Math.Clamp(x0 + dx, 0, width - 1);
                var sy = Math.Clamp(y0 + dy, 0, height - 1);
                var weight = (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy);
                result += frame[sy * width + sx] * weight;
            }
        }

        return result;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
}
