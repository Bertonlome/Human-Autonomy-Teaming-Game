using System;
using Game.Autoload;
using Game.Resources.Building;
using Godot;
using Game.Component;

namespace Game.UI;

public partial class UnitSection : PanelContainer
{
	[Signal]
	public delegate void SelectButtonPressedEventHandler();
	[Signal]
	public delegate void StopButtonPressedEventHandler();

	private BuildingResource buildingResource;

	private TextureRect robotIcon;
	private Label statusLabel;
	private Label anomalyLabel;
	private ProgressBar batteryBar;
	private Button selectButton;
	private Button stopButton;
	private Label batteryLabel;
	private BuildingComponent buildingComponent;
	private Panel panel;

	public enum RobotType
	{
		GroundRobot,
		AerialRobot
	}

	public override void _Ready()
	{
		robotIcon = GetNode<TextureRect>("%RobotIcon");
		statusLabel = GetNode<Label>("%StatusLabel");
		selectButton = GetNode<Button>("%SelectButton");
		stopButton = GetNode<Button>("%StopButton");
		batteryBar = GetNode<ProgressBar>("%BatteryBar");
		anomalyLabel = GetNode<Label>("%AnomalyLabel");
		batteryLabel = GetNode<Label>("%BatteryLabel");
		panel = GetNode<Panel>("%Panel");

		AudioHelpers.RegisterButtons(new Button[] { selectButton, stopButton });
		selectButton.Pressed += () => EmitSignal(SignalName.SelectButtonPressed);
		stopButton.Pressed += () => EmitSignal(SignalName.StopButtonPressed);

	}

	public void SetRobotType(BuildingComponent buildingComponent, BuildingResource resource, RobotType robotType)
	{
		buildingResource = resource;
		this.buildingComponent = buildingComponent;
		switch (robotType)
		{
			case UnitSection.RobotType.GroundRobot:
				robotIcon.Texture = ResourceLoader.Load<Texture2D>("res://assets/UGV.png");
				break;
			case UnitSection.RobotType.AerialRobot:
				robotIcon.Texture = ResourceLoader.Load<Texture2D>("res://assets/UAV.png");
				break;
		}
	}

	public void OnBatteryChange(int batteryPercentage)
	{
		int batteryValue = (int)Math.Round((batteryPercentage * 100.0) / buildingResource.BatteryMax);
		batteryBar.Value = batteryValue;
	}

	public void OnNewAnomalyReading(int anomalyValue)
	{
		anomalyLabel.Text = anomalyValue.ToString();
	}

	public void OnModeChanged(string mode)
	{
		statusLabel.Text = mode switch
		{
			"Random" => "Random Exploration",
			"GradientSearch" => "Gradient Search",
			"RewindMoves" => "Rewind Moves",
			"ReturnToBase" => "Returning to Base",
			"MoveToPos" => "Moving to Position",
			_ => "Idle"
		};
	}

	public void OnRobotStuck()
	{
		statusLabel.Text = "Stuck";
		statusLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0)); // Red color for stuck status
	}

	public void OnRobotUnStuck()
	{
		statusLabel.Text = "Idle";
		statusLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1)); // Reset to default color
	}

	public void OnStartCharging()
	{
		batteryLabel.Text = "Charging";
		batteryLabel.AddThemeColorOverride("font_color", new Color(0, 1, 0)); // Green color for charging status
	}

	public void OnStopCharging()
	{
		batteryLabel.Text = "Battery";
		statusLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1)); // Reset to default color
	}

	public void OnNewRobotSelected(BuildingComponent buildingComponent)
	{
		if (this.buildingComponent == buildingComponent)
		{
			panel.Visible = true; // Show the panel when this robot is selected
		}
		else
		{
			panel.Visible = false; // Hide the panel when another robot is selected
		}
	}

	public void OnNoMoreRobotSelected()
	{
		panel.Visible = false; // Hide the panel when no robot is selected
	}

}
