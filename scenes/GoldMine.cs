using Godot;

namespace Game;

public partial class GoldMine : Node2D
{
	[Export]
	private Texture2D activeTexture;

	private Sprite2D sprite;

	public override void _Ready()
	{
		sprite = GetNode<Sprite2D>("Sprite2D");
	}

	public void SetActive()
	{
		sprite.Texture = activeTexture;
	}
}
