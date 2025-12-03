using Godot;


namespace Game.UI;

public partial class FloatingText : Node2D
{
	public override void _Ready()
	{
		// Connect to animation finished signal to free the node after animation completes
		var animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");
		animationPlayer.AnimationFinished += OnAnimationFinished;
	}

	private void OnAnimationFinished(StringName animName)
	{
		QueueFree();
	}

	public void SetText(string text)
	{
		GetNode<Label>("%Label").Text = text;
	}
}
