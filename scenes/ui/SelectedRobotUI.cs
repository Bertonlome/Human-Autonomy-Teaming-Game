using System;
using System.Collections;
using System.Diagnostics.Tracing;
using Game.Autoload;
using Game.Component;
using Game.Resources.Building;
using Godot;

namespace Game.UI;

public partial class SelectedRobotUI : CanvasLayer
{
	[Export]
	BuildingResource bridgeBuildingResource;
	[Export]
	BuildingResource antennaBuildingResource;
	private Button randomExplorButton;
	private Button stopExplorbutton;
	private Button trackRobotButton;
	private Button gradientSearchButton;
	private Button returnToBaseButton;
	private Button startExplorButton;
	private OptionButton explorModeOptionsButton;
	private Label gravAnomValueLabel;
	private Label statusLabel;
	private Label batteryLabel;
	private Label resourceLabel;
	private Button multiPurposeButton;

	private Button placeAntennaButton;

	private MultiPurposeButtonState currentButtonState;
	public BuildingComponent selectedBuildingComponent;
	public BuildingComponent groundRobotBelowUav;

	public enum MultiPurposeButtonState
	{
		Placebridge,
		LiftRobot,
		DropRobot
	}

	public enum ExplorMode
	{
		Random,
		Gradient,
		ReturnToBase,
		None
	}

	private ExplorMode currentexplorMode = ExplorMode.None;

	public override void _Ready()
	{
		//InitializeUI();


		CallDeferred("SetAnomalySignal");
		CallDeferred("SetBatterySignal");
		CallDeferred("SetResourceSignal");
		GameEvents.Instance.Connect(GameEvents.SignalName.NoMoreRobotSelected, Callable.From<BuildingComponent>(OnNoMoreRobotSelected));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingStuck, Callable.From<BuildingComponent>(OnBuildingStuck));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingUnStuck, Callable.From<BuildingComponent>(OnBuildingUnStuck));
		GameEvents.Instance.Connect(GameEvents.SignalName.AllRobotStopped, Callable.From(OnAllRobotsStopped));
		GameEvents.Instance.Connect(GameEvents.SignalName.CarriedResourceCountChanged, Callable.From<int>(OnResourceCarriedCountChanged));
		GameEvents.Instance.Connect(GameEvents.SignalName.GroundRobotBelowUav, Callable.From<BuildingComponent>(OnGroundRobotBelowUav));
		GameEvents.Instance.Connect(GameEvents.SignalName.NoGroundRobotBelowUav, Callable.From(OnNoGroundRobotBelowUav));
	}

	public void SetupUI(BuildingComponent component)
	{
		selectedBuildingComponent = component;
		InitializeUI();
	}

	private void InitializeUI()
	{
		explorModeOptionsButton = GetNode<OptionButton>("%ExplorModeOptionsButton");
		randomExplorButton = GetNode<Button>("%RandomExplorButton");
		gradientSearchButton = GetNode<Button>("%GradientSearchButton");
		returnToBaseButton = GetNode<Button>("%ReturnToBaseButton");
		stopExplorbutton = GetNode<Button>("%StopExplorButton");
		trackRobotButton = GetNode<Button>("%TrackRobotButton");
		startExplorButton = GetNode<Button>("%StartExplorButton");
		multiPurposeButton = GetNode<Button>("%PlaceBridgeButton");
		placeAntennaButton = GetNode<Button>("%PlaceAntennaButton");
		gravAnomValueLabel = GetNode<Label>("%GravAnomValueLabel");
		statusLabel = GetNode<Label>("%StatusLabel");
		batteryLabel = GetNode<Label>("%BatteryLabel");
		resourceLabel = GetNode<Label>("%ResourceLabel");


		randomExplorButton.Pressed += OnRandomExplorButtonPressed;
		gradientSearchButton.Pressed += OnGradientSearchButtonPressed;
		returnToBaseButton.Pressed += OnReturnToBaseButtonPressed;
		stopExplorbutton.Pressed += OnStopExplorButtonPressed;
		trackRobotButton.Pressed += OnTrackRobotButtonPressed;
		explorModeOptionsButton.ItemSelected += OnOptionsButtonItemSelected;
		startExplorButton.Pressed += OnStartExplorButtonSelected;


		if (selectedBuildingComponent.BuildingResource.IsAerial)
		{
			if (selectedBuildingComponent.IsLifting)
			{
				ChangeStateMultiPurposeButton(MultiPurposeButtonState.DropRobot);
			}
			else
			{
				ChangeStateMultiPurposeButton(MultiPurposeButtonState.LiftRobot);
			}
			placeAntennaButton.Hide();
		}
		else
		{
			multiPurposeButton.Pressed += OnPlaceBridgeButtonPressed;
			placeAntennaButton.Pressed += OnPlaceAntennaButtonPressed;
		}
	}

	private void OnGroundRobotBelowUav(BuildingComponent groundRobot)
	{
		if (selectedBuildingComponent.BuildingResource.IsAerial)
		{
			multiPurposeButton.Disabled = false;
			groundRobotBelowUav = groundRobot;
		}
	}

	private void OnNoGroundRobotBelowUav()
	{
		if (selectedBuildingComponent.BuildingResource.IsAerial)
		{
			multiPurposeButton.Disabled = true;
		}
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


	private void OnOptionsButtonItemSelected(long index)
	{
		if (index == 0)
		{
			currentexplorMode = ExplorMode.Random;
		}
		else if (index == 1)
		{
			currentexplorMode = ExplorMode.Gradient;
		}
		else if (index == 2)
		{
			currentexplorMode = ExplorMode.ReturnToBase;
		}
	}

	private void OnStartExplorButtonSelected()
	{
		if (currentexplorMode == ExplorMode.Random || explorModeOptionsButton.Selected == 0) OnRandomExplorButtonPressed();
		else if (currentexplorMode == ExplorMode.Gradient || explorModeOptionsButton.Selected == 1) OnGradientSearchButtonPressed();
		else if (currentexplorMode == ExplorMode.ReturnToBase || explorModeOptionsButton.Selected == 2) OnReturnToBaseButtonPressed();
	}

	private void OnStopExplorButtonPressed()
	{
		currentexplorMode = ExplorMode.None;
		selectedBuildingComponent.StopAnyAutomatedMovementMode();
		statusLabel.Text = "Available";
	}

	private void OnTrackRobotButtonPressed()
	{
		if (trackRobotButton.Text == "Stop tracking")
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

	private void OnPlaceAntennaButtonPressed()
	{
		GameEvents.EmitPlaceAntennaButtonPressed(selectedBuildingComponent, antennaBuildingResource);
	}

	private void SetAnomalySignal()
	{
		selectedBuildingComponent.NewAnomalyReading += OnNewAnomalyReading;
		gravAnomValueLabel.Text = "Value: " + selectedBuildingComponent.GetAnomalyReadingAtCurrentPos();
		if (selectedBuildingComponent.IsStuck) { statusLabel.Text = "Stuck"; }
		else if (selectedBuildingComponent.currentExplorMode != BuildingComponent.ExplorMode.None) statusLabel.Text = "Busy";
		else statusLabel.Text = "Available";
	}

	private void SetBatterySignal()
	{
		selectedBuildingComponent.BatteryChange += OnBatteryChange;
		batteryLabel.Text = selectedBuildingComponent.Battery + " move left";
	}

	private void SetResourceSignal()
	{
		resourceLabel.Text = selectedBuildingComponent.resourceCollected.Count.ToString() + " / " + selectedBuildingComponent.BuildingResource.ResourceCapacity.ToString();
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
		if (multiPurposeButton.IsConnected("pressed", Callable.From(OnPlaceBridgeButtonPressed)))
		{
			multiPurposeButton.Pressed -= OnPlaceBridgeButtonPressed;
		}
		if (multiPurposeButton.IsConnected("pressed", Callable.From(OnLiftRobotButtonPressed)))
		{
			multiPurposeButton.Pressed -= OnLiftRobotButtonPressed;
		}
		if (multiPurposeButton.IsConnected("pressed", Callable.From(OnDropRobotButtonPressed)))
		{
			multiPurposeButton.Pressed -= OnDropRobotButtonPressed;
		}
		gradientSearchButton.Pressed -= OnGradientSearchButtonPressed;
		returnToBaseButton.Pressed -= OnReturnToBaseButtonPressed;
		explorModeOptionsButton.ItemSelected -= OnOptionsButtonItemSelected;
		startExplorButton.Pressed -= OnStartExplorButtonSelected;
		if(placeAntennaButton.IsConnected("pressed", Callable.From(OnPlaceAntennaButtonPressed)))
		{
			placeAntennaButton.Pressed -= OnPlaceAntennaButtonPressed;
		}
		if (selectedBuildingComponent != null)
		{
			selectedBuildingComponent.NewAnomalyReading -= OnNewAnomalyReading;
		}
	}

	private void OnPlaceBridgeButtonPressed()
	{
		GameEvents.EmitPlaceBridgeButtonPressed(selectedBuildingComponent, bridgeBuildingResource);
	}

	private void OnLiftRobotButtonPressed()
	{
		selectedBuildingComponent.AttachToRobot(groundRobotBelowUav);
		groundRobotBelowUav.AttachToRobot(selectedBuildingComponent);
		GameEvents.EmitLiftRobotButtonPressed(selectedBuildingComponent, groundRobotBelowUav);
		ChangeStateMultiPurposeButton(MultiPurposeButtonState.DropRobot);
	}

	private void OnDropRobotButtonPressed()
	{
		selectedBuildingComponent.DetachRobot();
		groundRobotBelowUav.DetachRobot();
		ChangeStateMultiPurposeButton(MultiPurposeButtonState.LiftRobot);
	}

	private void ChangeStateMultiPurposeButton(MultiPurposeButtonState state)
	{
		currentButtonState = state;
		switch (state)
		{
			case MultiPurposeButtonState.Placebridge:
				multiPurposeButton.Text = "Place Bridge";

				break;
			case MultiPurposeButtonState.LiftRobot:
				multiPurposeButton.Text = "Lift Robot";
				multiPurposeButton.Disabled = true;
				if (multiPurposeButton.IsConnected("pressed", Callable.From(OnDropRobotButtonPressed)))
				{
					multiPurposeButton.Pressed -= OnDropRobotButtonPressed;
				}
				multiPurposeButton.Pressed += OnLiftRobotButtonPressed;
				break;
			case MultiPurposeButtonState.DropRobot:
				multiPurposeButton.Text = "Drop Robot";
				if (multiPurposeButton.IsConnected("pressed", Callable.From(OnLiftRobotButtonPressed)))
				{
					multiPurposeButton.Pressed -= OnLiftRobotButtonPressed;
				}
				multiPurposeButton.Pressed += OnDropRobotButtonPressed;
				break;
		}
	}

}
