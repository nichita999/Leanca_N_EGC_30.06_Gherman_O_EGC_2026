using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

public class OrbitCamera : Camera
{
    public Vector3 Target { get; private set; }

    public float Distance { get; private set; }

    public float Azimuth { get; private set; }

    public float Elevation { get; private set; }

    public float MouseSensitivity = 0.2f;

    public float RotationSpeed = 90.0f;

    public float ZoomSpeed = 0.5f;

    public override string Name => "Camera Orbita";

    public OrbitCamera()
    {
        Reset();
    }

    // Poziția camerei în jurul țintei
    public override Vector3 Position
    {
        get
        {
            float az = MathHelper.DegreesToRadians(Azimuth);
            float el = MathHelper.DegreesToRadians(Elevation);

            float x = Distance *
                MathF.Cos(el) * MathF.Cos(az);

            float y = Distance * MathF.Sin(el);

            float z = Distance *
                MathF.Cos(el) * MathF.Sin(az);

            return Target + new Vector3(x, y, z);
        }
    }

    // Direcția spre țintă
    public override Vector3 Front =>
        Vector3.Normalize(Target - Position);

    // Matricea View
    public override Matrix4 ViewMatrix =>
        Matrix4.LookAt(
            Position,
            Target,
            Vector3.UnitY
        );

    public override void Update(
        KeyboardState keyboard,
        MouseState mouse,
        Vector2 mouseDelta,
        float dt)
    {
        // Rotire cu mouse-ul
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            Azimuth -= mouseDelta.X * MouseSensitivity;

            Elevation += mouseDelta.Y * MouseSensitivity;
        }

        // Rotire cu tastatura
        if (keyboard.IsKeyDown(Keys.Left))
            Azimuth += RotationSpeed * dt;

        if (keyboard.IsKeyDown(Keys.Right))
            Azimuth -= RotationSpeed * dt;

        if (keyboard.IsKeyDown(Keys.Up))
            Elevation += RotationSpeed * dt;

        if (keyboard.IsKeyDown(Keys.Down))
            Elevation -= RotationSpeed * dt;

        // Limitarea elevației
        Elevation = Math.Clamp(
            Elevation,
            -89.0f,
            89.0f
        );

        // Zoom: modificăm distanța, nu FOV-ul
        if (mouse.ScrollDelta.Y != 0)
        {
            Distance -= mouse.ScrollDelta.Y * ZoomSpeed;

            Distance = Math.Clamp(
                Distance,
                1.0f,
                30.0f
            );
        }
    }

    public override void Reset()
    {
        Target = Vector3.Zero;

        Distance = 8.0f;

        Azimuth = 90.0f;

        Elevation = 20.0f;

        FieldOfView = 60.0f;
    }

    public override IEnumerable<string> Describe()
    {
        yield return $"Tinta: {Target}";

        yield return $"Distanta: {Distance:F2}";

        yield return $"Azimut: {Azimuth:F2}";

        yield return $"Elevatie: {Elevation:F2}";

        yield return $"Pozitie: {Position}";
    }
}