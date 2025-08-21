using System;
using System.Collections;
using System.Diagnostics.Tracing;
using Game.Autoload;
using Game.Component;
using Godot;

namespace Game.UI;

public partial class SelectedRobotUI : CanvasLayer
{

	private Button randomExplorButton;
	private Button stopExplorbutton;
	private Button trackRobotButton;
	private Button gradientSearchButton;
	private Button returnToBaseButton;
	private Button rewindMovesButton;
	private Button startExplorButton;
	private OptionButton explorModeOptionsButton;
	private Label gravAnomValueLabel;
	private Label statusLabel;
	private Label batteryLabel;
	private Label resourceLabel;
	public BuildingComponent selectedBuildingComponent;

	public enum ExplorMode
	{
		Random,
		Gradient,
		ReturnToBase,
		Rewind,
		None
	}

	private ExplorMode currentexplorMode =  ExplorMode.None;

	public override void _Ready()
	{
		InitializeUI();


		CallDeferred("SetAnomalySignal");
		CallDeferred("SetBatterySignal");
		CallDeferred("SetResourceSignal");
		GameEvents.Instance.Connect(GameEvents.SignalName.NoMoreRobotSelected, Callable.From<BuildingComponent>(OnNoMoreRobotSelected));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingUnStuck, Callable.From<BuildingComponent>(OnBuildingUnStuck));
		GameEvents.Instance.Connect(GameEvents.SignalName.AllRobotStopped, Callable.From(OnAllRobotsStopped));
		GameEvents.Instance.Connect(GameEvents.SignalName.CarriedResourceCountChanged, Callable.From<int>(OnResourceCarriedCountChanged));
	}

	private void InitializeUI()
	{
		explorModeOptionsButton = GetNode<OptionButton>("%ExplorModeOptionsButton");
		randomExplorButton = GetNode<Button>("%RandomExplorButton");
		gradientSearchButton = GetNode<Button>("%GradientSearchButton");
		returnToBaseButton= GetNode<Button>("%ReturnToBaseButton");
		rewindMovesButton = GetNode<Button>("%RewindMovesButton");
		stopExplorbutton = GetNode<Button>("%StopExplorButton");
		trackRobotButton = GetNode<Button>("%TrackRobotButton");
		startExplorButton = GetNode<Button>("%StartExplorButton");
		gravAnomValueLabel = GetNode<Label>("%GravAnomValueLabel");
		statusLabel = GetNode<Label>("%StatusLabel");
		batteryLabel = GetNode<Label>("%BatteryLabel");
		resourceLabel = GetNode<Label>("%ResourceLabel");


		randomExplorButton.Pressed += OnRandomExplorButtonPressed;
		gradientSearchButton.Pressed += OnGradientSearchButtonPressed;
		returnToBaseButton.Pressed += OnReturnToBaseButtonPressed;
		rewindMovesButton.Pressed += OnRewindMovesButtonPressed;
		stopExplorbutton.Pressed += OnStopExplorButtonPressed;
		trackRobotButton.Pressed += OnTrackRobotButtonPressed;
		explorModeOptionsButton.ItemSelected += OnOptionsButtonItemSelected;
		startExplorButton.Pressed += OnStartExplorButtonSelected;
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
		statusLabel.Text = "Exploring randomly";
	}

	private void OnGradientSearchButtonPressed()
	{
		selectedBuildingComponent.EnableGradientSearchMode();
		statusLabel.Text = "Searching for high anomaly";
	}

	private void OnReturnToBaseButtonPressed()
	{
		selectedBuildingComponent.EnableReturnToBase();
		statusLabel.Text = "Returning to base";
	}

	private void OnRewindMovesButtonPressed()
	{
		selectedBuildingComponent.EnableRewindMovesMode();
		statusLabel.Text = "Returning to base";
	}

	private void OnOptionsButtonItemSelected(long index)
	{
		if(index == 0)
		{
			currentexplorMode = ExplorMode.Random;
		}
		else if(index == 1)
		{
			currentexplorMode = ExplorMode.Gradient;
		}
		else if(index == 2)
		{
			currentexplorMode = ExplorMode.Rewind;
		}
		else if(index == 3)
		{
			currentexplorMode = ExplorMode.ReturnToBase;
		}
	}

	private void OnStartExplorButtonSelected()
	{
		if(currentexplorMode == ExplorMode.Random || explorModeOptionsButton.Selected == 0) OnRandomExplorButtonPressed();
		else if(currentexplorMode == ExplorMode.Gradient || explorModeOptionsButton.Selected == 1) OnGradientSearchButtonPressed();
		else if(currentexplorMode == ExplorMode.Rewind || explorModeOptionsButton.Selected == 2) OnRewindMovesButtonPressed();
		else if(currentexplorMode == ExplorMode.ReturnToBase || explorModeOptionsButton.Selected == 3) OnReturnToBaseButtonPressed();
	}

	private void OnStopExplorButtonPressed()
	{
		currentexplorMode = ExplorMode.None;
		selectedBuildingComponent.StopAnyAutomatedMovementMode();
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
		else if(selectedBuildingComponent.currentExplorMode != BuildingComponent.ExplorMode.None) statusLabel.Text = "Busy";
		else statusLabel.Text = "Available";
	}

	private void SetBatterySignal()
	{
		selectedBuildingComponent.BatteryChange += OnBatteryChange;
		batteryLabel.Text = selectedBuildingComponent.Battery + " move left";
	}

	private void SetResourceSignal()
	{
		resourceLabel.Text = selectedBuildingComponent.resourceCollected.ToString() + " / " + selectedBuildingComponent.BuildingResource.ResourceCapacity.ToString();
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

	public void OnResourceCarriedCountChanged(int carriedResourceCount)
	{
		if (IsInstanceValid(resourceLabel))
		{
			resourceLabel.Text = carriedResourceCount.ToString() + " / " + selectedBuildingComponent.BuildingResource.ResourceCapacity.ToString();
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
