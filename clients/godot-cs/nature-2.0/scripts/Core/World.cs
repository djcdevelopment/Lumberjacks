using Godot;
using System.Collections.Generic;

namespace CommunitySurvival.Core;

/// <summary>
/// 3D world. Spawns entities from GameState signals. Placeholder for Slice 5 expansion.
/// </summary>
public partial class World : Node3D
{
    private GameState _state;
    private readonly Dictionary<string, Node3D> _entities = new();

    public override void _Ready()
    {
        _state = GetNode<GameState>("/root/GameState");
        _state.EntityAdded += OnAdded;
        _state.EntityChanged += OnChanged;
        _state.EntityRemoved += OnRemoved;

        GD.Print("World: ready");
        _state.ReplayEntities();
    }

    public override void _ExitTree()
    {
        _state.EntityAdded -= OnAdded;
        _state.EntityChanged -= OnChanged;
        _state.EntityRemoved -= OnRemoved;
    }

    private void OnAdded(string id, string type, Vector3 pos, float heading, Godot.Collections.Dictionary meta)
    {
        if (_entities.ContainsKey(id)) return;

        // For now, just log. Slice 5 will spawn actual scenes.
        GD.Print($"World: +{type} {id[..System.Math.Min(8, id.Length)]} at ({pos.X:F0},{pos.Y:F0},{pos.Z:F0})");

        // Placeholder: colored box so we can see something
        var node = new MeshInstance3D();
        var box = new BoxMesh();
        box.Size = type == "player" ? new Vector3(0.7f, 1.8f, 0.7f) : new Vector3(1f, 4f, 1f);
        node.Mesh = box;
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = type == "player" ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.13f, 0.4f, 0.13f);
        node.MaterialOverride = mat;
        node.GlobalPosition = pos;
        AddChild(node);
        _entities[id] = node;
    }

    private void OnChanged(string id, Vector3 pos, Vector3 vel, float heading, long tick)
    {
        if (_entities.TryGetValue(id, out var n))
            n.GlobalPosition = pos; // No interpolation yet — Slice 5
    }

    private void OnRemoved(string id)
    {
        if (_entities.Remove(id, out var n)) n.QueueFree();
    }
}
