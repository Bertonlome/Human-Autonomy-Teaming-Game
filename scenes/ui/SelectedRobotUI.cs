using System;
using System.Collections;
using Game.Autoload;
using Game.Component;
using Godot;

namespace Game.UI;

public partial class SelectedRobotUI : CanvasLayer
{
	private Button randomExplorButton;
	private Button stopExplorbutton;
	private Button trackRobotButton;
	public BuildingComponent selectedBuildingComponent;

	public override void _Ready()
	{
		randomExplorButton = GetNode<Button>("%RandomExplorButton");
		stopExplorbutton = GetNode<Button>("%StopExplorButton");
		trackRobotButton = GetNode<Button>("%TrackRobotButton");

		randomExplorButton.Pressed += OnRandomExplorButtonPressed;
		stopExplorbutton.Pressed += OnStopExplorButtonPressed;
		trackRobotButton.Pressed += OnTrackRobotButtonPressed;

		GameEvents.Instance.Connect(GameEvents.SignalName.NoMoreRobotSelected, Callable.From<BuildingComponent>(OnNoMoreRobotSelected));
	}

    private void OnNoMoreRobotSelected(BuildingComponent component)
    {
        QueueFree();
    }

    private void OnRandomExplorButtonPressed()
	{
		selectedBuildingComponent.IsRandomMode = true;
	}

	private void OnStopExplorButtonPressed()
	{
		selectedBuildingComponent.IsRandomMode = false;
	}

	private void OnTrackRobotButtonPressed()
	{
		if(trackRobotButton.Text == "Stop tracking")
		{
			SettingManager.EmitStopTrackingRobot();
			trackRobotButton.Text = "Track Robot";
		}
		else
		{
		SettingManager.EmitTrackingRobot(selectedBuildingComponent);
		trackRobotButton.Text = "Stop tracking";
		}
	}
}
