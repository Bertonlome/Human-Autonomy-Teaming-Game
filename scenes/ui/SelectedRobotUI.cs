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
	[Export]
	Texture2D mutedGeigerTexture;
	[Export]
	Texture2D unmutedGeigerTexture;
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
	private Label titleLabel;
	private Button multiPurposeButton;
	private Button toggleSoundGeigerButton;

	private Button placeAntennaButton;

	private MultiPurposeButtonState currentButtonState;
	public BuildingComponent selectedBuildingComponent;
	public BuildingComponent groundRobotBelowUav;
	private MiniMapController miniMapController;

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

	public void SetupUI(BuildingComponent component, GravitationalAnomalyMap anomalyMap)
	{
		selectedBuildingComponent = component;
		selectedBuildingComponent.ModeChanged += OnModeChanged;
		InitializeUI();
		
		
		// MiniMapController is optional - only initialize if it exists
		if (HasNode("%MiniMapController"))
		{
			miniMapController = GetNode<MiniMapController>("%MiniMapController");
			miniMapController.Initialize(selectedBuildingComponent, anomalyMap, anomalyMap.MapSize);
			
			// Set to robot-centered window mode with sensor radius
			int sensorRadius = selectedBuildingComponent.BuildingResource.AnomalySensorRadius;
			int windowSize = sensorRadius * 2; // diameter of sensor range
			miniMapController.SetMode(false, new Vector2I(windowSize, windowSize)); // false = robot window mode
			
			// Update robot position (this will refresh internally)
			miniMapController.SetRobotCell(selectedBuildingComponent.GetGridCellPosition());
			// Removed redundant Refresh() call - SetRobotCell already refreshes
			
			// Connect to building moved event to update minimap when robot moves
			GameEvents.Instance.Connect(GameEvents.SignalName.BuildingMoved, Callable.From<BuildingComponent>(OnBuildingMovedForMinimap));
		}
		else
		{
			GD.PrintErr("MiniMapController node not found in SelectedRobotUI scene.");
		}
		
		Visible = true;
	}

	private void InitializeUI()
	{
		explorModeOptionsButton = GetNode<OptionButton>("%ExplorModeOptionsButton");
		randomExplorButton = GetNode<Button>("%RandomExplorButton");
		if (!selectedBuildingComponent.BuildingResource.IsAerial)
		{
			explorModeOptionsButton.RemoveItem(0); // Removes the first item (index 0)
		}
		gradientSearchButton = GetNode<Button>("%GradientSearchButton");
		returnToBaseButton = GetNode<Button>("%ReturnToBaseButton");
		stopExplorbutton = GetNode<Button>("%StopExplorButton");
		trackRobotButton = GetNode<Button>("%TrackRobotButton");
		startExplorButton = GetNode<Button>("%StartExplorButton");
		multiPurposeButton = GetNode<Button>("%PlaceBridgeButton");
		placeAntennaButton = GetNode<Button>("%PlaceAntennaButton");
		toggleSoundGeigerButton = GetNode<Button>("%ToggleSoundGeigerButton");
		gravAnomValueLabel = GetNode<Label>("%GravAnomValueLabel");
		statusLabel = GetNode<Label>("%StatusLabel");
		batteryLabel = GetNode<Label>("%BatteryLabel");
		resourceLabel = GetNode<Label>("%ResourceLabel");
		titleLabel = GetNode<Label>("%Title");

		randomExplorButton.Pressed += OnRandomExplorButtonPressed;
		gradientSearchButton.Pressed += OnGradientSearchButtonPressed;
		returnToBaseButton.Pressed += OnReturnToBaseButtonPressed;
		stopExplorbutton.Pressed += OnStopExplorButtonPressed;
		trackRobotButton.Pressed += OnTrackRobotButtonPressed;
		toggleSoundGeigerButton.Pressed += () =>
		{
			if (!AudioHelpers.geigerActive)
			{
				AudioHelpers.StartGeigerCounter(initialAnomalyValue: selectedBuildingComponent.GetAnomalyReadingAtCurrentPos());
				toggleSoundGeigerButton.Icon = unmutedGeigerTexture; // Remove the muted icon
			}
			else
			{
				AudioHelpers.StopGeigerCounter();
				toggleSoundGeigerButton.Icon = mutedGeigerTexture; // Set the muted icon
			}
		};
		if (SettingManager.Instance.IsTrackingRobot)
		{
			trackRobotButton.Text = "Stop tracking";
		}
		else
		{
			trackRobotButton.Text = "Track Robot";
		}
		explorModeOptionsButton.ItemSelected += OnOptionsButtonItemSelected;
		startExplorButton.Pressed += OnStartExplorButtonSelected;


		if (selectedBuildingComponent.BuildingResource.IsAerial)
		{
			titleLabel.Text = "Selected Drone";
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
			titleLabel.Text = "Selected Rover";
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
		// Stop Geiger counter when robot is deselected
		AudioHelpers.StopGeigerCounter();
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
		int initialAnomaly = selectedBuildingComponent.GetAnomalyReadingAtCurrentPos();
		gravAnomValueLabel.Text = "Value: " + initialAnomaly;
		if (selectedBuildingComponent.IsStuck) { statusLabel.Text = "Stuck"; }
		else if (selectedBuildingComponent.currentExplorMode != BuildingComponent.ExplorMode.None) statusLabel.Text = "Busy";
		else statusLabel.Text = "Available";
		
		// Start Geiger counter with initial anomaly reading
		AudioHelpers.StartGeigerCounter(initialAnomaly);
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
		// Stop Geiger counter when UI is hidden
		AudioHelpers.StopGeigerCounter();
		Visible = false;
	}

	public void OnNewAnomalyReading(int value)
	{
		if (IsInstanceValid(gravAnomValueLabel))
		{
			gravAnomValueLabel.Text = "Value: " + value;
		}

		// Don't refresh minimap here - OnBuildingMovedForMinimap already handles it
		// This was causing double-refresh on every move
		
		// Update Geiger counter with new reading
		AudioHelpers.UpdateAnomalyReading(value);
	}

	private void OnBuildingMovedForMinimap(BuildingComponent movedBuilding)
	{
		// Only update if it's the selected robot that moved
		if (movedBuilding == selectedBuildingComponent && IsInstanceValid(miniMapController))
		{
			miniMapController.SetRobotCell(selectedBuildingComponent.GetGridCellPosition());
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
		if (gradientSearchButton.IsConnected("pressed", Callable.From(OnGradientSearchButtonPressed)))
		{
			gradientSearchButton.Pressed -= OnGradientSearchButtonPressed;
		}
		if (returnToBaseButton.IsConnected("pressed", Callable.From(OnReturnToBaseButtonPressed)))
		{
			returnToBaseButton.Pressed -= OnReturnToBaseButtonPressed;
		}
		if (explorModeOptionsButton.IsConnected("item_selected", Callable.From<long>(OnOptionsButtonItemSelected)))
		{
			explorModeOptionsButton.ItemSelected -= OnOptionsButtonItemSelected;
		}
		if (startExplorButton.IsConnected("pressed", Callable.From(OnStartExplorButtonSelected)))
		{
			startExplorButton.Pressed -= OnStartExplorButtonSelected;
		}
		if(placeAntennaButton.IsConnected("pressed", Callable.From(OnPlaceAntennaButtonPressed)))
		{
			placeAntennaButton.Pressed -= OnPlaceAntennaButtonPressed;
		}
		if (selectedBuildingComponent != null)
		{
			selectedBuildingComponent.NewAnomalyReading -= OnNewAnomalyReading;
		}
		
		// Disconnect minimap building moved event
		if (GameEvents.Instance != null && GameEvents.Instance.IsConnected(GameEvents.SignalName.BuildingMoved, Callable.From<BuildingComponent>(OnBuildingMovedForMinimap)))
		{
			GameEvents.Instance.Disconnect(GameEvents.SignalName.BuildingMoved, Callable.From<BuildingComponent>(OnBuildingMovedForMinimap));
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

	private void OnModeChanged(string mode)
	{
		if (IsInstanceValid(statusLabel))
		{
			if (mode == "Stuck")
			{
				statusLabel.Text = "Stuck";
			}
			else if (mode == "Available")
			{
				statusLabel.Text = "Available";
			}
			else if (mode == "Busy")
			{
				statusLabel.Text = "Busy";
			}
			else if (mode == "Reached Maxima")
			{
				statusLabel.Text = "Reached Maxima";
				AlertStatus();
			}
			else if (mode == "Idle")
			{
				statusLabel.Text = "Idle";
				AlertStatus();
			}
			else if (mode == "Lifting")
			{
				statusLabel.Text = "Lifting";
			}
			else if (mode == "Lifted")
			{
				statusLabel.Text = "Lifted";
			}
		}
	}

	private async void AlertStatus()
	{
		if (!IsInstanceValid(statusLabel)) return;
		
		// Pulse from white to red 3 times
		for (int i = 0; i < 3; i++)
		{
			// Fade from white to red
			for (float t = 0; t <= 1; t += 0.05f)
			{
				if (!IsInstanceValid(statusLabel)) return;
				Color color = new Color(1, 1 - t, 1 - t); // White (1,1,1) to Red (1,0,0)
				statusLabel.AddThemeColorOverride("font_color", color);
				await ToSignal(GetTree().CreateTimer(0.02f), "timeout");
			}
			
			// Fade from red back to white
			for (float t = 0; t <= 1; t += 0.05f)
			{
				if (!IsInstanceValid(statusLabel)) return;
				Color color = new Color(1, t, t); // Red (1,0,0) to White (1,1,1)
				statusLabel.AddThemeColorOverride("font_color", color);
				await ToSignal(GetTree().CreateTimer(0.02f), "timeout");
			}
		}
		
		// End with white color
		if (IsInstanceValid(statusLabel))
		{
			statusLabel.AddThemeColorOverride("font_color", Colors.White);
		}
	}


}
