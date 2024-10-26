using System.Collections.Generic;
using Godot;

namespace Game;

public static class Vector2IExtensions
{
    public static Vector2I ToBase64(this Vector2I vect)
    {
        return new Vector2I(vect.X / 64, vect.Y / 64);
    }
}
