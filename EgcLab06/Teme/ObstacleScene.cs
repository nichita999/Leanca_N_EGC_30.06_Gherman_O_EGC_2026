using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// TEMA - Varianta B: evită obstacolele. Cele trei benzi rămân fixe,
/// iar obstacolele înaintează pe axa Z spre jucător.
/// Totul este actualizat folosind exclusiv dt (timpul de joc).
/// </summary>
public sealed class ObstacleScene : Scene
{
    private const string Restart = "joc nou";
    private const float LaneWidth = 2.0f;
    private const float PlayerZ = 4.0f;
    private const float PlayerHeight = 0.9f;
    private const float LaneDuration = 0.15f;
    private const float Gravity = 20.0f;
    private const float JumpSpeed = 9.0f;
    private const float SpawnZ = -24.0f;
    private const float RoadLength = 42.0f;
    private const int MaxObstacles = 24;
    private readonly Random _random = new();
    private readonly List<Obstacle> _obstacles = new();
    private readonly List<SceneObject> _markings = new();
    private SceneObject _playerRoot = null!;
    private SceneObject _playerBody = null!;
    private SceneObject _shadow = null!;
    private Tween? _laneTween;
    private int _lane = 1;
    private float _jumpHeight, _verticalSpeed;
    private float _elapsed, _best, _spawnTimer, _stripeOffset;
    private bool _gameOver;

    private sealed class Obstacle
    {
        public SceneObject Object = null!;
        public bool Active;
        public float Height;
        public int Lane;
    }

    public override string Name => "tema B - evita obstacolele";
    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Left, Actions.Right, Actions.Jump, Restart
    };
    public override string Status => $"scor: {_elapsed:F1}s | record: {_best:F1}s | " +
        (_gameOver ? "GAME OVER - apasa R" : $"viteza: {WorldSpeed:F1}");
    private float WorldSpeed => 7.0f + MathF.Min(_elapsed * 0.34f, 17.0f);
    private float SpawnInterval => MathF.Max(0.62f, 1.65f - _elapsed * 0.017f);
    private static float LaneX(int lane) => (lane - 1) * LaneWidth;

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 8.0f, 16.0f);
        CameraTarget = new Vector3(0.0f, 0.1f, -8.0f);
        // Tastele sunt legate de acțiuni, nu interogăm direct KeyboardState.
        input.Bind(Actions.Left, Keys.A, Keys.Left);
        input.Bind(Actions.Right, Keys.D, Keys.Right);
        input.Bind(Actions.Jump, Keys.Space);
        input.Bind(Restart, Keys.R);

        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh disk = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));
        BuildRoad(cube);
        BuildPlayer(cube, disk);
        BuildObstacles(cube);
        ResetGame();
    }

    private SceneObject Box(string name, Mesh mesh, Vector3 position, Vector3 size, Color4 tint,
                            SceneObject? parent = null)
    {
        SceneObject obj = AddObject(name, mesh, parent);
        obj.Transform.Position = position;
        obj.Transform.Scale = size;
        obj.Tint = tint;
        return obj;
    }

    private void BuildRoad(Mesh cube)
    {
        Box("asfalt", cube, new Vector3(0, -0.12f, -8), new Vector3(6.0f, 0.24f, RoadLength),
            new Color4(0.18f, 0.20f, 0.25f, 1));
        for (int side = -1; side <= 1; side += 2)
        {
            Box("bordura", cube, new Vector3(side * 3.12f, 0.15f, -8),
                new Vector3(0.24f, 0.3f, RoadLength), new Color4(0.44f, 0.49f, 0.57f, 1));
        }
        // Bonus: marcajele se mișcă în direcția jucătorului și revin în fundal.
        for (int z = 0; z < 12; z++)
            foreach (float x in new[] { -1.0f, 1.0f })
                _markings.Add(Box("marcaj mobil", cube, new Vector3(x, 0.025f, -26.0f + z * 3.5f),
                    new Vector3(0.07f, 0.025f, 1.6f), new Color4(0.92f, 0.94f, 0.98f, 1)));
    }

    private void BuildPlayer(Mesh cube, Mesh disk)
    {
        // Ierarhie obligatorie: corpul este copilul pivotului jucătorului.
        // Pivotul controlează poziția pe bandă și săritura, corpul are transformare locală.
        _playerRoot = AddObject("pivot jucator", null);
        _playerRoot.Transform.Position = new Vector3(0, 0, PlayerZ);
        _playerBody = Box("jucator", cube, new Vector3(0, PlayerHeight * 0.5f, 0),
            new Vector3(0.85f, PlayerHeight, 0.85f), new Color4(0.2f, 0.55f, 1, 1), _playerRoot);
        _shadow = AddObject("umbra", disk);
        _shadow.Tint = new Color4(0.06f, 0.07f, 0.1f, 1);
        _shadow.Transform.Scale = new Vector3(1, 1, 1);
    }

    private void BuildObstacles(Mesh cube)
    {
        // Pool de obiecte: fără creare repetată de mesh-uri în timpul jocului.
        for (int i = 0; i < MaxObstacles; i++)
        {
            SceneObject obj = Box("obstacol", cube, new Vector3(0, 0, SpawnZ),
                Vector3.One, new Color4(1, 0.4f, 0.25f, 1));
            obj.Visible = false;
            _obstacles.Add(new Obstacle { Object = obj });
        }
    }

    private void ResetGame()
    {
        _lane = 1;
        _laneTween = null;
        _jumpHeight = _verticalSpeed = _elapsed = 0;
        _spawnTimer = 1.25f;
        _stripeOffset = 0;
        _gameOver = false;
        _playerRoot.Transform.Position = new Vector3(0, 0, PlayerZ);
        _playerBody.Tint = new Color4(0.2f, 0.55f, 1, 1);
        foreach (Obstacle o in _obstacles)
        {
            o.Active = false;
            o.Object.Visible = false;
        }
        for (int i = 0; i < _markings.Count; i++)
        {
            Vector3 pos = _markings[i].Transform.Position;
            pos.Z = -26.0f + (i / 2) * 3.5f;
            _markings[i].Transform.Position = pos;
        }
        UpdateShadow();
    }

    private void ChangeLane(int direction)
    {
        int next = _lane + direction;
        if (next < 0 || next > 2) return;
        _lane = next;
        Vector3 from = _playerRoot.Transform.Position;
        Vector3 to = new Vector3(LaneX(_lane), from.Y, PlayerZ);
        _laneTween = new Tween(from, to, LaneDuration, Easing.EaseOut);
    }

    private void SpawnObstacle()
    {
        Obstacle? o = _obstacles.FirstOrDefault(item => !item.Active);
        if (o == null) return;
        o.Active = true;
        o.Lane = _random.Next(3);
        o.Height = _random.NextDouble() < 0.60 ? 0.55f : 2.3f;
        o.Object.Transform.Position = new Vector3(LaneX(o.Lane), o.Height * 0.5f, SpawnZ);
        o.Object.Transform.Scale = new Vector3(1.1f, o.Height, 0.9f);
        o.Object.Tint = o.Height < 1.0f
            ? new Color4(1.0f, 0.65f, 0.18f, 1)
            : new Color4(0.95f, 0.22f, 0.30f, 1);
        o.Object.Visible = true;
    }

    // AABB: două cutii se ciocnesc doar dacă se suprapun pe X, Y și Z.
    private bool Collides(Obstacle o)
    {
        Vector3 p = _playerRoot.Transform.Position;
        Vector3 b = o.Object.Transform.Position;
        return MathF.Abs(p.X - b.X) < (0.85f + 1.1f) * 0.5f
            && (_jumpHeight < o.Height) // baza jucătorului sub partea de sus a obstacolului
            && MathF.Abs(PlayerZ - b.Z) < (0.85f + 0.9f) * 0.5f;
    }

    private void UpdateShadow()
    {
        Vector3 p = _playerRoot.Transform.Position;
        _shadow.Transform.Position = new Vector3(p.X, 0.02f, PlayerZ);
        float size = 0.95f / (1.0f + _jumpHeight * 0.24f);
        _shadow.Transform.Scale = new Vector3(size, 1, size);
    }

    public override void Update(float dt, Input input)
    {
        // R permite repornirea și după GAME OVER; în pauză nu avansăm simularea.
        if (input.WasPressed(Restart)) ResetGame();
        if (_gameOver || dt <= 0.0f) return;
        base.Update(dt, input);

        if (input.WasPressed(Actions.Left)) ChangeLane(-1);
        if (input.WasPressed(Actions.Right)) ChangeLane(1);
        if (_laneTween != null)
        {
            _laneTween.Update(dt);
            Vector3 p = _playerRoot.Transform.Position;
            p.X = _laneTween.Value.X;
            _playerRoot.Transform.Position = p;
            if (_laneTween.IsFinished) _laneTween = null;
        }

        if (input.WasPressed(Actions.Jump) && _jumpHeight <= 0.0f)
            _verticalSpeed = JumpSpeed;
        if (_verticalSpeed != 0.0f || _jumpHeight > 0.0f)
        {
            _verticalSpeed -= Gravity * dt;
            _jumpHeight += _verticalSpeed * dt;
            if (_jumpHeight <= 0)
            {
                _jumpHeight = 0;
                _verticalSpeed = 0;
            }
        }
        Vector3 playerPos = _playerRoot.Transform.Position;
        playerPos.Y = _jumpHeight;
        _playerRoot.Transform.Position = playerPos;
        UpdateShadow();

        _elapsed += dt;
        _best = MathF.Max(_best, _elapsed);
        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            SpawnObstacle();
            _spawnTimer += SpawnInterval;
        }

        float advance = WorldSpeed * dt;
        foreach (SceneObject line in _markings)
        {
            Vector3 p = line.Transform.Position;
            p.Z += advance;
            if (p.Z > 12.0f) p.Z -= 42.0f;
            line.Transform.Position = p;
        }
        foreach (Obstacle o in _obstacles)
        {
            if (!o.Active) continue;
            Vector3 p = o.Object.Transform.Position;
            p.Z += advance;
            o.Object.Transform.Position = p;
            if (Collides(o))
            {
                _gameOver = true;
                _playerBody.Tint = new Color4(1, 0.15f, 0.15f, 1);
                ConsolePanel.PrintMessage($"GAME OVER! Scor: {_elapsed:F1}s. R = joc nou");
                break;
            }
            if (p.Z > 12.0f)
            {
                o.Active = false;
                o.Object.Visible = false;
            }
        }
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string info in base.Describe()) yield return info;
        yield return $"Scor / record  : {_elapsed:F2} / {_best:F2} secunde";
        yield return $"Stare          : {(_gameOver ? "GAME OVER" : "joc in desfasurare")}";
        yield return $"Banda          : {_lane + 1} / 3; x={_playerRoot.Transform.Position.X:F2}";
        yield return $"Salt           : inaltime {_jumpHeight:F2}; vY={_verticalSpeed:F2}";
        yield return $"Viteza / spawn : {WorldSpeed:F2} u/s; {SpawnInterval:F2} s";
        yield return $"Obstacole      : {_obstacles.Count(o => o.Active)} active";
    }
}
