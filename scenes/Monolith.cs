using Godot;

namespace Game;

public partial class Monolith : Node2D
{
	[Export]
	private Texture2D activeTexture;

	private Node2D upDownRoot;
	private Sprite2D sprite;

	public override void _Ready()
	{
		sprite = GetNode<Sprite2D>("%MonolithSprite2D");
	}

	public void SetActive()
	{
		upDownRoot = GetNode<Node2D>("%UpDownRoot");
		sprite.Texture = activeTexture;
		var upDownTween = CreateTween();
		upDownTween.SetLoops(0);
		upDownTween.TweenProperty(upDownRoot, "position", Vector2.Down * 6, .3)
			.SetEase(Tween.EaseType.InOut)
			.SetTrans(Tween.TransitionType.Quad);
		upDownTween.TweenProperty(upDownRoot, "position", Vector2.Up * 6, .3)
			.SetEase(Tween.EaseType.InOut)
			.SetTrans(Tween.TransitionType.Quad); ;
	}

	public void SetVisible()
	{
		this.Visible = true;
	}
}
