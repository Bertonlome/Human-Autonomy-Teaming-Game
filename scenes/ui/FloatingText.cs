using Godot;


namespace Game.UI;

public partial class FloatingText : Node2D
{

	public void SetText(string text)
	{
		GetNode<Label>("%Label").Text = text;
	}
}
