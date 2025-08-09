using Godot;
using Game.Autoload;

namespace Game.UI;

public partial class RulesMenu : CanvasLayer
{

	[Signal]
	public delegate void DonePressedEventHandler();
	public override void _Ready()
	{
		var doneButton = GetNode<Button>("%DoneButton");
		AudioHelpers.RegisterButtons(new Button[] { doneButton });
		doneButton.Pressed += OnDoneButtonPressed;
	}
	
	private void OnDoneButtonPressed()
	{
		EmitSignal(SignalName.DonePressed);
	}
}
