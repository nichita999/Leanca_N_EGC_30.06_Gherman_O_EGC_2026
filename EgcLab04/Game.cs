using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Fereastra aplicației și bucla principală (main game loop).
/// Față de laboratorul 3: o scenă cu mai multe obiecte, fiecare cu matricea lui model,
/// o cameră cu matricele view și projection, tastatura și mouse-ul.
/// </summary>
public class Game : GameWindow
{
    // Obiecte care NU depind de OpenGL pot fi create imediat.
    private readonly Camera _camera = new();

    // Obiectele care folosesc OpenGL se creează în OnLoad (abia atunci există contextul OpenGL).
    private Shader _shader = null!;
    private readonly List<SceneObject> _scene = new();
    private readonly List<Transform2D> _initialTransforms = new();   // pentru tasta R

    // Starea aplicației (se modifică în OnUpdateFrame, se folosește în OnRenderFrame).
    private int _selected;            // indexul obiectului selectat în _scene (tasta Tab)
    private bool _wireframe;          // contur (true) sau plin (false)

    // T4: Punctul in jurul caruia se roteste obiectul.
    private Vector2 _pivot = Vector2.Zero;

    // T5: Satelitul si unghiul orbitei.
    private Mesh _satellite = null!;
    private float _orbitAngle = 0.0f;

    // Contoare afișate în titlul ferestrei.
    private readonly string _baseTitle;
    private double _secondTimer;
    private int _frames, _lastFrames;
    private bool _titleDirty = true;

    public Game(int width, int height, string title)
        : base(GameWindowSettings.Default,
               new NativeWindowSettings
               {
                   ClientSize = new Vector2i(width, height),
                   Title = title,
                   APIVersion = new Version(3, 3),          // cerem OpenGL 3.3 ...
                   Profile = ContextProfile.Core,           // ... profil Core (fără modul imediat)
                   Flags = ContextFlags.ForwardCompatible
               })
    {
        _baseTitle = title;
        VSync = VSyncMode.On;   // swap-ul bufferelor așteaptă reîmprospătarea monitorului
    }

    /// <summary>Apelată O SINGURĂ DATĂ, după crearea ferestrei și a contextului OpenGL.</summary>
    protected override void OnLoad()
    {
        base.OnLoad();

        Console.WriteLine($"OpenGL  : {GL.GetString(StringName.Version)}");
        Console.WriteLine($"Renderer: {GL.GetString(StringName.Renderer)}");

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        _camera.Resize(ClientSize.X, ClientSize.Y);

        _shader = Shader.FromFiles("Shaders/shader.vert", "Shaders/shader.frag");

        // Scena: aceleași forme din laboratorul 3, acum plasate în lume prin Transform2D.
        // Depth (z) contează doar în perspectivă (tasta O, secțiunea 7.2).
        AddObject("pătrat", Shapes.Quad(),
                  new Transform2D { Position = new Vector2(-1.8f, -1.0f), Depth = -3.0f });
        AddObject("triunghi", Shapes.Triangle(),
          new Transform2D
          {
              Position = new Vector2(-1.2f, 1.0f),
              Rotation = 45,
              Scale = new Vector2(1.5f, 0.5f)
          });
        AddObject("hexagon", Shapes.RegularPolygon(6, 0.5f),
                  new Transform2D { Position = new Vector2(2.0f, 1.0f) });
        AddObject("cerc", Shapes.RegularPolygon(64, 0.5f),
                  new Transform2D { Position = new Vector2(0.8f, -0.6f), Depth = 2.0f });

        // T5: Cream un triunghi care va orbita obiectul selectat.
        _satellite = Shapes.Triangle();
    }

    /// <summary>Adaugă un obiect în scenă și reține transformarea lui inițială (pentru tasta R).</summary>
    private void AddObject(string name, Mesh mesh, Transform2D transform)
    {
        _scene.Add(new SceneObject(name, mesh, transform));
        _initialTransforms.Add(transform.Clone());
    }

    /// <summary>Apelată când se schimbă dimensiunea ferestrei.</summary>
    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);   // zona ferestrei în care se desenează
        _camera.Resize(e.Width, e.Height);      // proiecția se adaptează la noul raport de aspect
    }

    /// <summary>Apelată la fiecare iterație a buclei: LOGICA aplicației (intrări, mișcare, timp).</summary>
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (KeyboardState.IsKeyDown(Keys.Escape))
        {
            Close();
        }

        // --- Obiectul selectat: tastatura (secțiunea 8) ------------------------------

        float dt = (float)args.Time;

        // T5: O rotatie completa (360 grade) in 4 secunde.
        float orbitSpeed = MathHelper.TwoPi / 4.0f;

        _orbitAngle += orbitSpeed * dt;

        // Pastram unghiul intre 0 si 2 * PI.
        if (_orbitAngle >= MathHelper.TwoPi)
            _orbitAngle %= MathHelper.TwoPi;

        SceneObject sel = _scene[_selected];

        // T2: Shift mareste viteza deplasarii, rotatiei si scalarii de 3 ori.
        float speedMultiplier = KeyboardState.IsKeyDown(Keys.LeftShift) ? 3.0f : 1.0f;

        float moveSpeed = 2.0f * speedMultiplier;

        if (KeyboardState.IsKeyDown(Keys.Left))
            sel.Transform.Position.X -= moveSpeed * dt;

        if (KeyboardState.IsKeyDown(Keys.Right))
            sel.Transform.Position.X += moveSpeed * dt;

        if (KeyboardState.IsKeyDown(Keys.Down))
            sel.Transform.Position.Y -= moveSpeed * dt;

        if (KeyboardState.IsKeyDown(Keys.Up))
            sel.Transform.Position.Y += moveSpeed * dt;

        // T2: Rotatia obiectului cu 90 de grade pe secunda.
        float rotationSpeed = 90.0f * speedMultiplier;

        if (KeyboardState.IsKeyDown(Keys.Q))
            sel.Transform.Rotation += rotationSpeed * dt;

        if (KeyboardState.IsKeyDown(Keys.E))
            sel.Transform.Rotation -= rotationSpeed * dt;


        // T2: Scalarea uniforma a obiectului.
        float scaleSpeed = MathF.Pow(1.5f, dt * speedMultiplier);

        if (KeyboardState.IsKeyDown(Keys.Z))
            sel.Transform.Scale /= scaleSpeed;

        if (KeyboardState.IsKeyDown(Keys.X))
            sel.Transform.Scale *= scaleSpeed;


        // T2: Limitam scara intre 0.1 si 10.
        sel.Transform.Scale = new Vector2(
            Math.Clamp(sel.Transform.Scale.X, 0.1f, 10.0f),
            Math.Clamp(sel.Transform.Scale.Y, 0.1f, 10.0f)
        );

        // T2: Scalarea se face prin inmultire deoarece reprezinta
        // o modificare proportionala a dimensiunilor obiectului.
        // Adunarea ar modifica dimensiunea cu o valoare fixa,
        // nu cu un factor proportional.

        if (KeyboardState.IsKeyPressed(Keys.Tab)) _selected = (_selected + 1) % _scene.Count;
        if (KeyboardState.IsKeyPressed(Keys.O))   _camera.IsPerspective = !_camera.IsPerspective;
        if (KeyboardState.IsKeyPressed(Keys.F))   _wireframe = !_wireframe;
        if (KeyboardState.IsKeyPressed(Keys.R))   ResetScene();

        if (KeyboardState.IsKeyPressed(Keys.Tab) || KeyboardState.IsKeyPressed(Keys.O) ||
            KeyboardState.IsKeyPressed(Keys.R))
        {
            _titleDirty = true;
        }

        // --- Obiectul selectat: mouse-ul (secțiunea 9) -------------------------------

        // T3: Mutam obiectul selectat la pozitia cursorului.
        if (MouseState.IsButtonPressed(MouseButton.Left))
        {
            Vector2 mouseWorld = ScreenToWorld(MouseState.Position);

            sel.Transform.Position = mouseWorld;

            // T3: Modificam doar pozitia X si Y.
            // Adancimea (Depth) obiectului ramane neschimbata.
        }

        // T4: La clic dreapta stabilim pivotul in pozitia cursorului.
        if (MouseState.IsButtonPressed(MouseButton.Right))
        {
            _pivot = ScreenToWorld(MouseState.Position);
        }

        // T4: Rotim obiectul selectat in jurul pivotului.
        if (KeyboardState.IsKeyDown(Keys.P))
        {
            float angle = MathHelper.DegreesToRadians(90.0f * dt);

            // Calculam pozitia obiectului fata de pivot.
            Vector2 relative = sel.Transform.Position - _pivot;

            // Rotim vectorul in jurul originii.
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            Vector2 rotated = new Vector2(
                relative.X * cos - relative.Y * sin,
                relative.X * sin + relative.Y * cos
            );

            // Readucem obiectul in sistemul de coordonate al lumii.
            sel.Transform.Position = _pivot + rotated;

            // Rotim si obiectul in jurul propriului centru.
            sel.Transform.Rotation += 90.0f * dt;
        }

        // T4: Scadem coordonatele pivotului pentru a efectua
        // rotatia fata de origine, apoi adaugam pivotul inapoi.
        // Modificam si unghiul propriu pentru a pastra orientarea
        // obiectului fata de pivot, asemenea Lunii fata de Pamant.

        // --- Camera: tastatura și rotița (secțiunea 6.2) -----------------------------

        float camSpeed = 3.0f / _camera.Zoom;                    // unități de lume pe secundă
        if (KeyboardState.IsKeyDown(Keys.A)) _camera.Position.X -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.D)) _camera.Position.X += camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.S)) _camera.Position.Y -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.W)) _camera.Position.Y += camSpeed * dt;

        _camera.Zoom *= MathF.Pow(1.1f, MouseState.ScrollDelta.Y);   // un pas al rotiței = ±10 %
        _camera.Zoom = Math.Clamp(_camera.Zoom, 0.1f, 10.0f);

        // --- Titlul ferestrei: obiectul selectat, proiecția, cadre pe secundă --------

        _secondTimer += args.Time;
        if (_secondTimer >= 1.0)
        {
            _lastFrames = _frames;
            _frames = 0;
            _secondTimer -= 1.0;
            _titleDirty = true;
        }

        if (_titleDirty)
        {
            string projection = _camera.IsPerspective ? "perspectivă" : "ortografică";
            Title = $"{_baseTitle} | selectat: {_scene[_selected].Name} | proiecție {projection} | FPS: {_lastFrames}";
            _titleDirty = false;
        }
    }

    /// <summary>Apelată la fiecare iterație a buclei: DESENAREA unui cadru.</summary>
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        _frames++;

        // 1. Ștergem imaginea veche din back buffer.
        GL.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        // 2. Starea de rasterizare și programul shader.
        SetWireframe(_wireframe);
        _shader.Use();

        // 3. View și projection sunt aceleași pentru toate obiectele: le trimitem o singură dată.
        _shader.SetMatrix4("uView", _camera.ViewMatrix);
        _shader.SetMatrix4("uProjection", _camera.ProjectionMatrix);

        // 4. Fiecare obiect: matricea lui model, nuanța, apoi desenarea.
        foreach (SceneObject obj in _scene)
        {
            _shader.SetMatrix4("uModel", obj.Transform.ModelMatrix);
            _shader.SetColor("uTint", obj.Tint);
            obj.Mesh.Draw();
        }

        // T5: Matricea locala a satelitului.
        // Intai scalare, apoi translatie, apoi rotatie.
        Matrix4 satelliteLocal =
            Matrix4.CreateScale(0.3f) *
            Matrix4.CreateTranslation(1.2f, 0.0f, 0.0f) *
            Matrix4.CreateRotationZ(_orbitAngle);

        // T5: Inmultim matricea locala cu matricea obiectului selectat.
        Matrix4 satelliteModel =
            satelliteLocal * _scene[_selected].Transform.ModelMatrix;

        // T5: Satelitul mosteneste pozitia, rotatia si scalarea obiectului selectat.
        _shader.SetMatrix4("uModel", satelliteModel);

        // Stabilim culoarea satelitului.
        _shader.SetColor("uTint", Color4.Yellow);

        // Desenam satelitul.
        _satellite.Draw();

        // T5: Cand obiectul selectat este scalat, se modifica si orbita
        // satelitului. Daca obiectul este marit, creste si distanta
        // satelitului fata de centru. Daca este micsorat, distanta scade.
        // Acest lucru se intampla deoarece matricea model a satelitului
        // include matricea model a obiectului selectat, inclusiv scalarea.
        // O scalare neuniforma transforma orbita circulara intr-o elipsa.

        // 5. Afișăm cadrul terminat: back buffer <-> front buffer.
        SwapBuffers();
    }

    /// <summary>Apelată O SINGURĂ DATĂ, la închiderea ferestrei: eliberăm resursele GPU.</summary>
    protected override void OnUnload()
    {
        foreach (SceneObject obj in _scene)
        {
            obj.Dispose();
        }

        // T5: Eliberam resursele GPU folosite de satelit.
        _satellite.Dispose();

        _shader.Dispose();

        base.OnUnload();
    }

    /// <summary>Transformă poziția mouse-ului (pixeli, origine stânga-sus) în coordonate de lume.</summary>
    private Vector2 ScreenToWorld(Vector2 mousePixels)
    {
        // T3: Convertim coordonatele mouse-ului din pixeli in NDC.
        float x = 2.0f * mousePixels.X / ClientSize.X - 1.0f;
        float y = 1.0f - 2.0f * mousePixels.Y / ClientSize.Y;

        // T3: Axa Y a mouse-ului creste in jos, iar in NDC creste in sus.
        // De aceea folosim 1 - pentru a inversa directia axei Y.

        Vector4 ndc = new Vector4(x, y, 0.0f, 1.0f);

        // T3: Calculam inversa matricelor view si projection.
        Matrix4 inverse = Matrix4.Invert(
            _camera.ViewMatrix * _camera.ProjectionMatrix
        );

        // T3: Transformam coordonatele NDC in coordonate de lume.
        Vector4 world = Vector4.TransformRow(ndc, inverse);

        return new Vector2(world.X / world.W, world.Y / world.W);
    }

    /// <summary>Tasta R: obiectele revin la transformările inițiale, camera la starea inițială.</summary>
    private void ResetScene()
    {
        for (int i = 0; i < _scene.Count; i++)
        {
            _scene[i].Transform = _initialTransforms[i].Clone();
        }
        _camera.Reset();
    }

    /// <summary>
    /// Contur / plin. Dacă Visual Studio semnalează o eroare la primul parametru,
    /// folosiți aici tipul acceptat de versiunea voastră (vezi laboratorul 2, Problema 3):
    /// TriangleFace.FrontAndBack sau MaterialFace.FrontAndBack.
    /// </summary>
    private static void SetWireframe(bool wireframe)
    {
        GL.PolygonMode(TriangleFace.FrontAndBack, wireframe ? PolygonMode.Line : PolygonMode.Fill);
    }
}
