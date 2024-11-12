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
	private Label gravAnomValueLabel;
	private Label statusLabel;
	private Label batteryLabel;
	public BuildingComponent selectedBuildingComponent;

	public override void _Ready()
	{
		InitializeUI();


		CallDeferred("SetAnomalySignal");
		CallDeferred("SetBatterySignal");
		GameEvents.Instance.Connect(GameEvents.SignalName.NoMoreRobotSelected, Callable.From<BuildingComponent>(OnNoMoreRobotSelected));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingUnStuck, Callable.From<BuildingComponent>(OnBuildingUnStuck));
		GameEvents.Instance.Connect(GameEvents.SignalName.AllRobotStopped, Callable.From(OnAllRobotsStopped));
	}

	private void InitializeUI()
	{
		randomExplorButton = GetNode<Button>("%RandomExplorButton");
		stopExplorbutton = GetNode<Button>("%StopExplorButton");
		trackRobotButton = GetNode<Button>("%TrackRobotButton");
		gravAnomValueLabel = GetNode<Label>("%GravAnomValueLabel");
		statusLabel = GetNode<Label>("%StatusLabel");
		batteryLabel = GetNode<Label>("%BatteryLabel");


		randomExplorButton.Pressed += OnRandomExplorButtonPressed;
		stopExplorbutton.Pressed += OnStopExplorButtonPressed;
		trackRobotButton.Pressed += OnTrackRobotButtonPressed;
	}

    private void OnNoMoreRobotSelected(BuildingComponent component)
    {
		DisconnectSignals();
        QueueFree();
    }

	private void OnBuildingStuck(BuildingComponent component)
	{
		statusLabel.Text = "Stuck";
	}

	private void OnBuildingUnStuck(BuildingComponent component)
	{
		statusLabel.Text = "Available";
	}

	private void OnAllRobotsStopped()
	{
		statusLabel.Text = "Available";
	}

    private void OnRandomExplorButtonPressed()
	{
		selectedBuildingComponent.EnableRandomMode();
		statusLabel.Text = "Busy";
	}

	private void OnStopExplorButtonPressed()
	{
		selectedBuildingComponent.StopRandomMode();
		statusLabel.Text = "Available";
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

	private void SetAnomalySignal()
	{
		selectedBuildingComponent.NewAnomalyReading += OnNewAnomalyReading;
		gravAnomValueLabel.Text = "Value: " + selectedBuildingComponent.GetAnomalyReadingAtCurrentPos(); 
		if (selectedBuildingComponent.IsStuck){statusLabel.Text = "Stuck";}
		else if(selectedBuildingComponent.IsRandomMode) statusLabel.Text = "Busy";
		else statusLabel.Text = "Available";
	}

	private void SetBatterySignal()
	{
		selectedBuildingComponent.BatteryChange += OnBatteryChange;
		batteryLabel.Text = selectedBuildingComponent.Battery + " move left";
	}

	public void OnBatteryChange(int value)
	{
		if (IsInstanceValid(batteryLabel))
		{
			batteryLabel.Text = value + " move left";
		}
	}

	public void HideUI()
	{
		Visible = false;
	}

	public void OnNewAnomalyReading(int value)
	{
		if (IsInstanceValid(gravAnomValueLabel))
		{
			gravAnomValueLabel.Text = "Value: " + value;
		}
	}

	private void DisconnectSignals()
    {
        // Safely disconnect signals before the object is freed
        randomExplorButton.Pressed -= OnRandomExplorButtonPressed;
        stopExplorbutton.Pressed -= OnStopExplorButtonPressed;
        trackRobotButton.Pressed -= OnTrackRobotButtonPressed;

        if (selectedBuildingComponent != null)
        {
            selectedBuildingComponent.NewAnomalyReading -= OnNewAnomalyReading;
        }
    }

}
