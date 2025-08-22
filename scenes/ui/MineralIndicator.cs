using Godot;


namespace Game.UI;
public partial class MineralIndicator : Node2D
{

	private Sprite2D Sprite2D;
	private Tween activeTween;
	[Export]
	private Texture2D redOreTexture;
	[Export]
	private Texture2D greenOreTexture;
	[Export]
	private Texture2D blueOreTexture;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Sprite2D = GetNode<Sprite2D>("%Sprite2D");

		var duration = GD.RandRange(.4, .5);

		activeTween = CreateTween();
		activeTween.SetLoops();
		activeTween.TweenProperty(Sprite2D, "position", Vector2.Up * 4, duration)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.InOut);
		activeTween.TweenProperty(Sprite2D, "position", Vector2.Down * 4, duration)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.InOut);
	}

	public void Destroy()
	{
		if (activeTween != null && activeTween.IsValid())
		{
			activeTween.Kill();
		}

		activeTween = CreateTween();
		activeTween.SetParallel();
		activeTween.TweenInterval(GD.RandRange(.1, .3));
		activeTween.Chain();
		activeTween.TweenProperty(Sprite2D, "scale", Vector2.Zero, .3)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.In);
		activeTween.TweenProperty(Sprite2D, "position", Vector2.Up * 32, .3)
			.SetTrans(Tween.TransitionType.Quad)
			.SetEase(Tween.EaseType.In);
		activeTween.Chain();
		activeTween.TweenCallback(Callable.From(() => QueueFree()));
	}

	public void SetOreType(string oreType)
	{
		if (Sprite2D == null)
		{
			Sprite2D = GetNode<Sprite2D>("%Sprite2D");
			if (Sprite2D == null)
			{
				GD.PrintErr("Sprite2D not found in MineralIndicator!");
				return;
			}
		}
		switch (oreType)
		{
			case "red_ore":
				Sprite2D.Texture = redOreTexture;
				break;
			case "green_ore":
				Sprite2D.Texture = greenOreTexture;
				break;
			case "blue_ore":
				Sprite2D.Texture = blueOreTexture;
				break;
		}
	}
}
