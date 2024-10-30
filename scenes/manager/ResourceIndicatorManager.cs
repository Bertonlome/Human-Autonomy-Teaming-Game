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
	private Dictionary<Vector2I, Resourceindicator> tileToresourceIndicator = new();

	private HashSet<Vector2I> indicatedTiles = new();

	private AudioStreamPlayer audioStreamPlayer;


	public override void _Ready()
	{
		audioStreamPlayer = GetNode<AudioStreamPlayer>("AudioStreamPlayer");
		gridManager.ResourceTilesUpdated += OnResourceTilesUpdated;
	}

	private void UpdateIndicators(IEnumerable<Vector2I> newIndicatedTiles, IEnumerable<Vector2I> toRemoveTiles)
	{

		if(newIndicatedTiles.Any())
		{
			audioStreamPlayer.Play();
		}
		foreach (var newTile in newIndicatedTiles)
		{
			var indicator = resourceIndicatorScene.Instantiate<Resourceindicator>();
			AddChild(indicator);
			indicator.GlobalPosition = newTile * 64;
			tileToresourceIndicator[newTile] = indicator;
		}

		foreach(var removeTile in toRemoveTiles)
		{
			tileToresourceIndicator.TryGetValue(removeTile, out var indicator);
			if(IsInstanceValid(indicator))
			{
				indicator.Destroy();
			}
			tileToresourceIndicator.Remove(removeTile);
		}
	}

	private void HandleResourceTilesUpdated()
	{
		GD.Print("TEST");
		var currentResourceTiles = gridManager.GetCollectedResourcetiles();
		var newlyIndicatedTiles = currentResourceTiles.Except(indicatedTiles);
		var toRemoveTiles = indicatedTiles.Except(currentResourceTiles);
		indicatedTiles = currentResourceTiles;
		UpdateIndicators(newlyIndicatedTiles, toRemoveTiles);
	}

	private void OnResourceTilesUpdated(int _)
	{
		Callable.From(HandleResourceTilesUpdated).CallDeferred();
	}

}
