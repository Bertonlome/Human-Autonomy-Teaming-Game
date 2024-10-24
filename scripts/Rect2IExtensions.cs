using System.Collections.Generic;
using Godot;

namespace Game;

public static class Rect2IExtensions
{
    public static List<Vector2I> ToTiles(this Rect2I rect)
    {
        var tiles = new List<Vector2I>();
        for (int x = rect.Position.X; x < rect.End.X; x++)
        {
            for (int y = rect.Position.Y; y < rect.End.Y; y++)
            {
                tiles.Add(new Vector2I(x, y));
            }
        }
        return tiles;
    }

    public static Rect2 ToRect2F(this Rect2I rect)
    {
        return new Rect2(rect.Position, rect.Size);
    }
}
