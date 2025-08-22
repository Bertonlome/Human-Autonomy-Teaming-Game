using System.Collections.Generic;
using System.Linq;
using Game.UI;
using Godot;

namespace Game.Manager;

public partial class ResourceIndicatorManager : Node
{

	[Export]
	private GridManager gridManager;
	[Export]
	private PackedScene resourceIndicatorScene;
	[Export]
	private PackedScene mineralIndicatorScene;
	private Dictionary<Vector2I, Resourceindicator> tileToresourceIndicator = new();
	private Dictionary<Vector2I, MineralIndicator> tileTomineraIndicator = new();

	private HashSet<Vector2I> indicatedTiles = new();

	private AudioStreamPlayer audioStreamPlayer;


	public override void _Ready()
	{
		audioStreamPlayer = GetNode<AudioStreamPlayer>("AudioStreamPlayer");
		gridManager.ResourceTilesUpdated += OnResourceTilesUpdated;
		gridManager.MineralTilesUpdated += OnResourceTilesUpdated;
	}

	private void UpdateIndicators(IEnumerable<Vector2I> newIndicatedTiles, IEnumerable<Vector2I> toRemoveTiles, string resourceType)
	{

		if (newIndicatedTiles.Any())
		{
			audioStreamPlayer.Play();
		}
		foreach (var newTile in newIndicatedTiles)
		{
			MineralIndicator indicator;
			Resourceindicator resourceIndicator;
			if (resourceType == "red_ore" || resourceType == "green_ore" || resourceType == "blue_ore")
			{
				// Instantiate as MineralIndicator
				var mineralIndicator = mineralIndicatorScene.Instantiate<MineralIndicator>();
				mineralIndicator.SetOreType(resourceType);
				indicator = mineralIndicator;
				AddChild(indicator);
				indicator.GlobalPosition = newTile * 64;
				tileTomineraIndicator[newTile] = indicator;
			}
			else if (resourceType == "wood")
			{
				resourceIndicator = resourceIndicatorScene.Instantiate<Resourceindicator>();
				AddChild(resourceIndicator);
				resourceIndicator.GlobalPosition = newTile * 64;
				tileToresourceIndicator[newTile] = resourceIndicator;
			}

		}

		foreach (var removeTile in toRemoveTiles)
		{
			tileToresourceIndicator.TryGetValue(removeTile, out var indicator);
			if (IsInstanceValid(indicator))
			{
				indicator.Destroy();
			}
			tileToresourceIndicator.Remove(removeTile);
		}
	}

	private void HandleResourceTilesUpdated(string resourceType)
	{
		var currentResourceTiles = gridManager.GetCollectedResourcetiles();
		var newlyIndicatedTiles = currentResourceTiles.Except(indicatedTiles);
		var toRemoveTiles = indicatedTiles.Except(currentResourceTiles);
		indicatedTiles = currentResourceTiles;
		UpdateIndicators(newlyIndicatedTiles, toRemoveTiles, resourceType);
	}

	private void OnResourceTilesUpdated(int _, string resourceType)
	{
		Callable.From(() => HandleResourceTilesUpdated(resourceType)).CallDeferred();
	}

}
