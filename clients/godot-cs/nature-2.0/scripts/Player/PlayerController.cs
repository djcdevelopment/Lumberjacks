using Godot;
using CommunitySurvival.Networking;
using CommunitySurvival.Core;

namespace CommunitySurvival.Player;

/// <summary>
/// Attached to local player only. Captures WASD, sends binary PlayerInput at 20Hz.
/// </summary>
public partial class PlayerController : Node
{
    private SimulationClient _net;
    private ushort _seq;
    private double _timer;
    private const double Interval = 0.05; // 20Hz

    public override void _Ready()
    {
        _net = GetNode<SimulationClient>("/root/SimulationClient");
    }

    public override void _Process(double delta)
    {
        _timer += delta;
        if (_timer < Interval) return;
        _timer = 0;

        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");

        byte dir = 0;
        byte speed = 0;
        byte action = 0;

        if (input.Length() > 0.1f)
        {
            speed = 100;
            // atan2(x, -y): 0 = forward (-Z in Godot = North on server)
            float angle = Mathf.Atan2(input.X, -input.Y);
            dir = CoordinateMapper.GodotRotationToServerByte(angle);
        }

        if (Input.IsActionPressed("interact"))
            action |= 0x04;

        _seq++;
        _ = _net.SendPlayerInput(dir, speed, action, _seq);
    }
}
