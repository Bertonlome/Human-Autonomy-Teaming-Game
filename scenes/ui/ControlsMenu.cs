using Game.Autoload;
using Godot;
using System;

public partial class ControlsMenu : CanvasLayer
{
	private Button doneButton;
	[Signal]
	public delegate void DonePressedEventHandler();
	public override void _Ready()
	{
		doneButton = GetNode<Button>("%DoneButton");
		doneButton.Pressed += OnDoneButtonPressed;
		AudioHelpers.RegisterButtons(new Button[] { doneButton });
	}

	public void OnDoneButtonPressed()
	{
		EmitSignal(SignalName.DonePressed);
	}
}
