using Godot;

namespace CommunitySurvival.Entities;

/// <summary>
/// Base class for network-synced entities. Smooth interpolation per ADR 0017.
/// Adaptive lerp speed: 20Hz near zone gets fast lerp, 5Hz mid zone slows down
/// to avoid snapping to target and waiting.
/// </summary>
public partial class RemoteEntity : Node3D
{
    [Export] public float InterpolationSpeed = 10.0f;

    protected Vector3 _targetPosition;
    protected float _targetRotationY;
    private double _lastUpdateTime;
    private double _updateInterval = 0.05; // 20Hz default

    public void Initialize(Vector3 position, float headingRad)
    {
        GlobalPosition = position;
        _targetPosition = position;
        Rotation = new Vector3(0, headingRad, 0);
        _targetRotationY = headingRad;
    }

    public void UpdateFromServer(Vector3 position, Vector3 velocity, float headingRad, long tick)
    {
        _targetPosition = position;
        _targetRotationY = headingRad;

        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastUpdateTime > 0)
            _updateInterval = now - _lastUpdateTime;
        _lastUpdateTime = now;
    }

    public override void _Process(double delta)
    {
        float alpha = (float)(delta * InterpolationSpeed);

        // ADR 0017: slow lerp for 5Hz zones to avoid reaching target too early
        if (_updateInterval > 0.1)
            alpha *= (float)(0.05 / _updateInterval);

        GlobalPosition = GlobalPosition.Lerp(_targetPosition, alpha);
        Rotation = new Vector3(
            Rotation.X,
            Mathf.LerpAngle(Rotation.Y, _targetRotationY, alpha),
            Rotation.Z);
    }
}
