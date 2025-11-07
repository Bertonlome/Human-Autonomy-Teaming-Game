using Godot;

namespace Game.Building;

public partial class PaintedTile : Node2D
{
	private ColorRect colorRect;
	private Label tileNumberLabel;
	private LineEdit labelEdit;

	public override void _Ready()
	{
		colorRect = GetNode<ColorRect>("ColorRect");
		tileNumberLabel = GetNode<Label>("TileNumberLabel");
		labelEdit = GetNode<LineEdit>("LabelEdit");
	}

	public void SetColor(Color color)
	{
		color.A = 0.5f; // Set to 50% transparency
		colorRect.Color = color;
	}

	public void SetNumberLabel(int number)
	{
		tileNumberLabel.Text = number.ToString();
	}

	public void DisplayLabelEdit()
	{
		labelEdit.Visible = true;
		labelEdit.GrabFocus();
	}

}
