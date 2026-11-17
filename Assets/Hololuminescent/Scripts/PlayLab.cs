using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Wake the Lab, the playful touch games (the version of branch airi-exhibit-playful, kept beside the official exhibit
/// PlayLab), built at runtime from the showcase's models, the story's department models and the 3D headlines
/// (SourceModels/Story/make_story_assets.py, Lab_* and Hint_*). Numpad 7 starts it (SceneSwitcher); Esc leaves it.
///
/// The opening (also the resting state): tap to raise the AIRI building storey by storey, swipe across the ribbon to cut
/// it, confetti and "Welcome to AIRI". Then the menu: Play the lab (left) or AIRI Research (right), five challenges each,
/// one progress dot each, back to the menu at the end. 40 s without a touch: back to the opening.
///  Play:     heart (tap with the beat), engine (circle the fan), orbit (flick the moons), stallion (swipe to gallop),
///            globe (tap your home; pins kept between visitors), then the lit core.
///  Research: robotics (drag the arm, box onto the pad), autonomous (fly the drone through rings), data labelling (tap to
///            label), cybersecurity (tap the red attacks), 5G-SHARE (drag links from the mast), then AIRI x UTCN, join us.
/// </summary>
public class PlayLab : MonoBehaviour
{
    // Same frame as the spots (StoryDirector): heights above the stage, which is Lift above the alcove floor.
    private const float Lift = 1.0f, Mid = 1.25f, TextY = 2.95f, HintY = 2.3f, TextWidth = 2.1f, TextForward = 0.6f;
    private const float DotsY = 0.12f;
    private const float IdleSeconds = 40f, EndIdleSeconds = 20f;

    private ObjectSwitcher switcher;
    private DisplayAlignment display;
    private StoryDirector story;
    private Light alcoveSpot;
    private float alcoveSpotIntensity;
    private Renderer ledgeLook, floorLook;
    private bool swayWas, running, timedOut;
    private GameObject keyLight;
    private Camera cam;
    private Vector3 center;
    private Material dark, red, light;
    private int lit;
    private float lastTouch;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly Dictionary<GameObject, List<(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)>> letters =
        new Dictionary<GameObject, List<(Transform, Vector3, Quaternion, Vector3)>>();

    // ---------------------------------------------------------------- fingers (touch, mouse as one finger)

    private class Finger
    {
        public int id;
        public Vector2 pos, start, delta;
        public float since;
        public bool began, ended;
        public int seen;
    }

    private readonly Dictionary<int, Finger> fingerById = new Dictionary<int, Finger>();
    private readonly List<Finger> fingers = new List<Finger>();
    private float lastRealTouch = -10f;

    private void ReadFingers()
    {
        foreach (var f in fingers) if (f.ended) fingerById.Remove(f.id);
        int frame = Time.frameCount;
        bool touching = false;
        var screen = Touchscreen.current;
        if (screen != null)
            foreach (var t in screen.touches)
            {
                bool down = t.press.wasPressedThisFrame, up = t.press.wasReleasedThisFrame;
                if (!t.press.isPressed && !up) continue;
                touching = true;
                Seen(t.touchId.ReadValue(), t.position.ReadValue(), up, frame);
            }
        if (touching) lastRealTouch = Time.time;
        // Windows also turns touches into mouse clicks: the mouse only counts a while after the last real touch.
        var mouse = Mouse.current;
        if (!touching && Time.time - lastRealTouch > 0.5f && mouse != null &&
            (mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame))
            Seen(-1, mouse.position.ReadValue(), mouse.leftButton.wasReleasedThisFrame, frame);

        fingers.Clear();
        foreach (var f in fingerById.Values)
        {
            if (f.seen != frame) { f.ended = true; f.began = false; f.delta = Vector2.zero; } // lost: ends now
            fingers.Add(f);
        }
        if (fingers.Count > 0) lastTouch = Time.time;
    }

    private void Seen(int id, Vector2 pos, bool up, int frame)
    {
        if (!fingerById.TryGetValue(id, out var f))
        {
            f = new Finger { id = id, pos = pos, start = pos, since = Time.time, began = true };
            fingerById[id] = f;
        }
        else
        {
            f.began = false;
            f.delta = pos - f.pos;
            f.pos = pos;
        }
        f.ended = up;
        f.seen = frame;
    }

    // How far open (0..1): two fingers spread apart (it closes when they lift), or the mouse wheel, which stays.
    private float wheelOpen;

    private float Spread(ref float pinch0)
    {
        if (Held() >= 2)
        {
            float d = Vector2.Distance(fingers[0].pos, fingers[1].pos);
            if (pinch0 < 0f) pinch0 = d;
            wheelOpen = 0f;
            return Mathf.Clamp01((d - pinch0) / (Screen.height * 0.25f));
        }
        pinch0 = -1f;
        var mouse = Mouse.current;
        if (mouse != null) wheelOpen = Mathf.Clamp01(wheelOpen + Mathf.Sign(mouse.scroll.ReadValue().y) * (mouse.scroll.ReadValue().y != 0f ? 0.2f : 0f));
        return wheelOpen;
    }

    private int Held()
    {
        int n = 0;
        foreach (var f in fingers) if (!f.ended) n++;
        return n;
    }

    // ---------------------------------------------------------------- start, leave

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        Attach();
        SceneManager.sceneLoaded += (s, m) => Attach();
    }

    private static void Attach()
    {
        var showcase = FindAnyObjectByType<ObjectSwitcher>();
        if (showcase != null && showcase.GetComponent<PlayLab>() == null)
            showcase.gameObject.AddComponent<PlayLab>();
    }

    private void Start()
    {
        switcher = GetComponent<ObjectSwitcher>();
        display = FindAnyObjectByType<DisplayAlignment>();
        story = GetComponent<StoryDirector>();
        dark = Resources.Load<Material>("Models/UTCN/UTCN_Dark");
        red = Resources.Load<Material>("Models/UTCN/UTCN_Red");
        light = new Material(dark);
        light.SetColor("_BaseColor", new Color(0.78f, 0.78f, 0.8f));
        var spot = GameObject.Find("Spot Light");
        alcoveSpot = spot != null ? spot.GetComponent<Light>() : null;
    }

    public void Begin()
    {
        if (!running && (story == null || !story.Running)) StartCoroutine(Enter());
    }

    private void Update()
    {
        ReadFingers();
        var keyboard = Keyboard.current;
        bool toggle = false;   // numpad 7 starts it through SceneSwitcher (Begin); the four-finger hold is the exhibit's

        if (running && story != null && story.Running) { Leave(false); return; }   // a spot took over
        if (running && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) toggle = true;
        if (running && keyboard != null && keyboard.nKey.wasPressedThisFrame) skip = true;
        if (!toggle) return;
        if (running) Leave(true);
        else if (story == null || !story.Running) StartCoroutine(Enter());
    }

    private IEnumerator Enter()
    {
        running = true;
        switcher.enabled = false;
        yield return null;   // after ObjectSwitcher.Start has shown its first object
        switcher.HideAll();
        cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        if (display != null) { display.HelpVisible = false; swayWas = display.Sway; display.Sway = false; }
        Measure();
        AddKeyLight();
        var ledge = GameObject.Find("Ledge");
        ledgeLook = ledge != null ? ledge.GetComponent<Renderer>() : null;
        if (ledgeLook != null) ledgeLook.enabled = false;
        var mirror = GameObject.Find("Mirror");   // shows as a grey band on the HLD (see StoryDirector)
        floorLook = mirror != null ? mirror.GetComponent<Renderer>() : null;
        if (floorLook != null) floorLook.enabled = false;
        lastTouch = Time.time;
        StartCoroutine(Loop());
    }

    private void Leave(bool showcase)
    {
        StopAllCoroutines();
        Clear();
        if (keyLight != null)
        {
            if (alcoveSpot != null) alcoveSpot.intensity = alcoveSpotIntensity;
            Destroy(keyLight);
        }
        keyLight = null;
        if (ledgeLook != null) ledgeLook.enabled = true;
        if (floorLook != null) floorLook.enabled = true;
        ledgeLook = floorLook = null;
        if (display != null && swayWas) display.Sway = true;
        swayWas = false;
        running = false;
        if (!showcase) return;
        switcher.HideAll();
        switcher.ShowFirst();
        switcher.enabled = true;
    }

    private void OnDestroy() { if (running) Leave(false); }

    private void Measure()
    {
        float depth = PlayerPrefs.GetFloat("HLD_ObjectDepth", -1.5f);   // GesturePlayer's shared W / S depth
        Vector3 spot = switcher.Spot;
        float floor = Physics.Raycast(spot + Vector3.up * 5f, Vector3.down, out var hit, 50f) ? hit.point.y : spot.y - 1f;
        center = new Vector3(spot.x, floor + Lift, spot.z + depth);
    }

    // Same as the spots: a copy of the alcove's Spot Light from in front and above, so every model throws its shadow.
    private void AddKeyLight()
    {
        if (alcoveSpot == null) return;
        alcoveSpotIntensity = alcoveSpot.intensity;
        keyLight = Instantiate(alcoveSpot.gameObject);
        keyLight.name = "Lab Key Light";
        keyLight.transform.position = center + new Vector3(-0.8f, 3.8f, -3.0f);
        keyLight.transform.rotation = Quaternion.LookRotation(center + Vector3.up * 1.2f - keyLight.transform.position);
        var l = keyLight.GetComponent<Light>();
        l.intensity = alcoveSpotIntensity;
        l.shadows = LightShadows.Soft;
        alcoveSpot.intensity = alcoveSpotIntensity * 0.3f;
    }

    // ---------------------------------------------------------------- the game

    // The opening (raise the institute, cut the ribbon) is also the resting state; then the menu and its two paths,
    // back to the menu after each; 40 s without a touch anywhere starts over from the opening.
    private IEnumerator Loop()
    {
        timedOut = false;
        yield return Ceremony();
        while (!timedOut)
        {
            bool research = false;
            yield return Menu(r => research = r);
            if (timedOut) break;
            yield return research ? ResearchPath() : PlayPath();
        }
        StopAllCoroutines();   // dots, bursts; this one ends at the yield below
        Clear();
        StartCoroutine(Loop());
    }

    private IEnumerator PlayPath()
    {
        var dots = Dots();
        foreach (var station in new Func<IEnumerator>[] { HeartStation, EngineStation, OrbitStation, HorseStation, GlobeStation })
        {
            yield return station();
            if (timedOut) break;
            lit++;
        }
        if (!timedOut) yield return Finale();
        StopCoroutine(dots);
        Clear();
    }

    private IEnumerator ResearchPath()
    {
        var dots = Dots();
        foreach (var station in new Func<IEnumerator>[] { ArmStation, DroneStation, LabelStation, CyberStation, FiveGStation })
        {
            yield return station();
            if (timedOut) break;
            lit++;
        }
        if (!timedOut) yield return ResearchEnd();
        StopCoroutine(dots);
        Clear();
    }

    // Five progress dots at the bottom, the current one pulsing.
    private Coroutine Dots()
    {
        lit = 0;
        var dots = Row(new Vector3(-0.44f, DotsY, 0f), new Vector3(0.44f, DotsY, 0f), 5, 0.075f);
        foreach (var d in dots) keep.Add(d.gameObject);
        return StartCoroutine(PulseDots(dots));
    }

    private readonly HashSet<GameObject> keep = new HashSet<GameObject>();   // what stays from station to station

    // N: on to the next station (for trying the game out).
    private bool skip;
    private bool TakeSkip() { bool s = skip; skip = false; return s; }

    private bool Idle(float seconds = IdleSeconds)
    {
        if (Time.time - lastTouch < seconds) return false;
        timedOut = true;
        return true;
    }

    private IEnumerator PulseDots(List<Renderer> dots)
    {
        while (true)
        {
            for (int i = 0; i < dots.Count; i++)
            {
                if (dots[i] == null) continue;
                dots[i].sharedMaterial = i < lit ? red : i == lit ? dark : light;
                float s = i == lit ? 1f + 0.25f * Mathf.Sin(Time.time * 6f) : 1f;
                dots[i].transform.localScale = Vector3.one * 0.075f * s;
            }
            yield return null;
        }
    }

    // ------------------------------------------------ 1 heart: tap with the beat

    private IEnumerator HeartStation()
    {
        var heart = Model("Models/HumanHeart/HumanHeart", 1.3f, 1.5f, 0.62f);
        var pivot = Pivot(heart);
        Vector3 mid = pivot.position;
        var open = Opener(heart, mid, 0.35f, 0.9f);
        var title = Text("Lab_Heart", TextY);
        var hint = Text("Hint_Heart", HintY, true);
        var ring = Arc(mid, 0.95f, 36, 0.045f, 90f, 360f);
        var ecg = new Ecg(this, new Vector3(center.x, center.y + 0.36f, center.z - 0.35f), 2.0f);

        const float period = 0.6f, latency = 0.05f;   // 100 bpm; the touch reaches the app a little late
        const int need = 10;
        wheelOpen = 0f;
        float t0 = Time.time, squeeze = 0f, wobble = 0f, explode = 0f, pinch0 = -1f;
        int hits = 0;
        bool hintSwapped = false;
        while (hits < need)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            float dt = Time.deltaTime, phase = (Time.time - t0) % period;
            float cue = Mathf.Exp(-phase * 9f);
            Swell(ring, 1f + 0.7f * cue);   // the beat to tap with: the ring swells, the heart twitches a little

            if (Held() <= 1)
                foreach (var f in fingers)
                {
                    if (!f.began) continue;
                    float p = (Time.time - latency - t0) % period, err = Mathf.Min(p, period - p);
                    // a miss only shakes it: losing progress made it feel stuck
                    if (err < 0.2f) { hits++; squeeze = 1f; ecg.Beat(1f); }
                    else { wobble = 1f; ecg.Beat(0.2f); }
                    Fill(ring, hits / (float)need);
                }

            float target = Spread(ref pinch0);
            explode = Mathf.MoveTowards(explode, target, dt * 2.5f);
            open(explode);

            squeeze = Mathf.MoveTowards(squeeze, 0f, dt * 5f);
            wobble = Mathf.MoveTowards(wobble, 0f, dt * 3f);
            pivot.localScale = Vector3.one * (1f - 0.13f * squeeze - 0.03f * cue * (1f - squeeze));
            pivot.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 45f) * 7f * wobble, 0f);
            ecg.Tick(dt);

            if (!hintSwapped && hits >= 5)
            {
                hintSwapped = true;
                TextOut(hint);
                hint = Text("Hint_Open", HintY, true);
            }
            yield return null;
        }

        Burst(mid, 36, red, 2f);
        TextOut(title);
        TextOut(hint);
        var done = Text("Lab_HeartDone", TextY);
        for (float t = 0f, beat = 0f; t < 3.2f; t += Time.deltaTime, beat += Time.deltaTime)
        {
            if (beat > 0.83f) { beat = 0f; squeeze = 1f; ecg.Beat(1f); }   // on its own now, 72 bpm
            squeeze = Mathf.MoveTowards(squeeze, 0f, Time.deltaTime * 5f);
            pivot.localScale = Vector3.one * (1f - 0.13f * squeeze);
            explode = Mathf.MoveTowards(explode, 0f, Time.deltaTime * 2f);
            open(explode);
            Swell(ring, 1f + 0.5f * squeeze);
            ecg.Tick(Time.deltaTime);
            yield return null;
        }
        TextOut(done);
        yield return ShrinkAll(0.5f);
    }

    // ------------------------------------------------ 2 engine: draw circles around the fan

    private IEnumerator EngineStation()
    {
        var engine = Model("Models/Engine/source/engine_sk", 1.3f, 2.0f, 0.6f);
        Transform rotor = FindPart(engine.transform, "ROTOR");
        var rotorLook = rotor != null ? rotor.GetComponent<Renderer>() : null;
        if (rotorLook != null)
        {
            // Turn it so the fan faces the viewer, a little from the side: the 3D reads, and circling the fan feels right.
            Vector3 toFan = rotorLook.bounds.center - Middle(engine);
            toFan.y = 0f;
            Vector3 want = Quaternion.Euler(0f, 35f, 0f) * Vector3.back;
            engine.transform.RotateAround(Middle(engine), Vector3.up, Vector3.SignedAngle(toFan, want, Vector3.up));
            Fit(engine, 1.3f, 2.0f, 0.6f);
        }
        var pivot = Pivot(engine);
        Vector3 mid = pivot.position;
        Vector3 fan = rotorLook != null ? rotorLook.bounds.center : mid;
        Vector3 axis = (fan - mid).sqrMagnitude > 1e-6f ? (fan - mid).normalized : Vector3.back;
        Vector3 fanLocal = pivot.InverseTransformPoint(fan), axisLocal = pivot.InverseTransformDirection(axis);
        Vector3 rear = mid - axis * Vector3.Dot(GesturePlayer.GetBounds(Parts(engine)).extents, Abs(axis));

        var title = Text("Lab_Engine", TextY);
        var hint = Text("Hint_Engine", HintY, true);
        const float zone = 0.75f;
        var gauge = Arc(new Vector3(fan.x, fan.y, center.z - 0.9f), 0.78f, 34, 0.042f, 225f, -270f);   // a dial, clockwise
        var streaks = new List<(Transform t, Vector3 v, float life)>();

        float power = 0f, inZone = 0f;
        while (inZone < 3f)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            float dt = Time.deltaTime;
            Vector2 c = cam.WorldToScreenPoint(fan);
            float near = Screen.height * 0.04f;
            foreach (var f in fingers)
            {
                if (f.began) continue;
                Vector2 a = f.pos - f.delta - c, b = f.pos - c;
                if (a.magnitude < near || b.magnitude < near) continue;
                power += Mathf.Abs(Vector2.SignedAngle(a, b)) / 360f * 0.16f;   // about six circles to the red
            }
            power = Mathf.Clamp(power - dt * 0.1f * (0.5f + power), 0f, 1.15f);
            if (power >= zone) inZone += dt;
            Fill(gauge, power, zone);
            Swell(gauge, power >= zone ? 1f + 0.25f * Mathf.Sin(Time.time * 14f) : 1f);

            if (rotor != null)   // up to 7 turns a second
                rotor.RotateAround(pivot.TransformPoint(fanLocal), pivot.TransformDirection(axisLocal), power * 7f * 360f * dt);
            pivot.position = mid + UnityEngine.Random.insideUnitSphere * 0.008f * power * power;

            if (power > 0.3f && UnityEngine.Random.value < power * 0.9f)   // exhaust
            {
                var s = Prim(PrimitiveType.Cube, "Exhaust", light, false);
                s.transform.position = rear + UnityEngine.Random.insideUnitSphere * 0.12f;
                s.transform.rotation = Quaternion.LookRotation(-axis);
                s.transform.localScale = new Vector3(0.012f, 0.012f, 0.12f + 0.25f * power);
                streaks.Add((s.transform, -axis * (1.5f + 3f * power), 0.45f));
            }
            for (int i = streaks.Count - 1; i >= 0; i--)
            {
                var (t, v, life) = streaks[i];
                life -= dt;
                if (t == null || life <= 0f) { if (t != null) Kill(t.gameObject); streaks.RemoveAt(i); continue; }
                t.position += v * dt;
                streaks[i] = (t, v, life);
            }
            yield return null;
        }

        Burst(fan, 36, red, 2.2f);
        TextOut(title);
        TextOut(hint);
        var done = Text("Lab_EngineDone", TextY);
        for (float t = 0f; t < 2.8f; t += Time.deltaTime)
        {
            if (rotor != null)
                rotor.RotateAround(pivot.TransformPoint(fanLocal), pivot.TransformDirection(axisLocal), 7f * 360f * Time.deltaTime);
            pivot.position = mid + UnityEngine.Random.insideUnitSphere * 0.006f;
            yield return null;
        }
        foreach (var s in streaks) if (s.t != null) Kill(s.t.gameObject);
        TextOut(done);
        yield return ShrinkAll(0.5f);
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // ------------------------------------------------ 3 orbit: flick the moons round the planet

    private class Moon
    {
        public Transform t;
        public Renderer look;
        public TrailRenderer trail;
        public Vector2 p, v, dock;
        public float r, swept, back;
        public int finger = int.MinValue;   // the finger holding it
        public bool flying, stable;
    }

    private IEnumerator OrbitStation()
    {
        var system = Load("Models/PlanetSystem/PlanetSystem");
        var moons = new List<Moon>();
        float[] sizes = { 0.2f, 0.15f, 0.11f };
        for (int i = 0; i < 3; i++)
        {
            var t = FindPart(system.transform, "moon " + (i + 1));
            if (t == null) continue;
            t.SetParent(null, true);
            Track(t.gameObject);
            var look = t.GetComponent<Renderer>();
            if (look == null) continue;
            t.localScale *= sizes[i] / Mathf.Max(0.0001f, look.bounds.size.x);
            moons.Add(new Moon { t = t, look = look, r = sizes[i] * 0.5f });
        }
        Fit(system, 0.85f, 0.85f, Mid - 0.42f);   // what is left: the planet and its clouds
        var planet = Pivot(system);
        Vector3 origin = planet.position;
        float planetR = 0.42f;

        var title = Text("Lab_Orbit", TextY);
        var hint = Text("Hint_Orbit", HintY, true);
        var count = Row(new Vector3(-0.14f, HintY - 0.32f, 0f), new Vector3(0.14f, HintY - 0.32f, 0f), 3, 0.06f);
        Fill(count, 0f);

        const float gm = 0.55f;
        Vector2[] docks = { new Vector2(-0.75f, -0.92f), new Vector2(0f, -1.0f), new Vector2(0.75f, -0.92f) };
        for (int i = 0; i < moons.Count; i++)
        {
            var m = moons[i];
            m.p = m.dock = docks[i];
            m.trail = m.t.gameObject.AddComponent<TrailRenderer>();
            m.trail.sharedMaterial = red;
            m.trail.time = 1.4f;
            m.trail.widthMultiplier = 0.02f;
            m.trail.generateLightingData = true;
            m.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m.trail.emitting = false;
            Place(m, origin);
        }

        int stable = 0;
        while (stable < 3)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            float dt = Time.deltaTime;
            planet.Rotate(Vector3.up, 12f * dt, Space.World);

            foreach (var f in fingers)
            {
                if (f.began)
                {
                    Moon best = null;
                    float bestD = Screen.height * 0.08f;
                    foreach (var m in moons)
                    {
                        if (m.stable || m.finger != int.MinValue) continue;
                        float d = Vector2.Distance(f.pos, cam.WorldToScreenPoint(m.look.bounds.center));
                        if (d < bestD) { best = m; bestD = d; }
                    }
                    if (best != null) { best.finger = f.id; best.flying = false; best.back = 0f; best.trail.emitting = false; best.trail.Clear(); }
                }
            }

            foreach (var m in moons)
            {
                if (m.stable && !m.flying) continue;
                Finger held = null;
                if (m.finger != int.MinValue) fingerById.TryGetValue(m.finger, out held);
                if (held != null && !held.ended)
                {
                    Vector3 w = OnPlane(held.pos, origin.z) - origin;
                    var target = new Vector2(w.x, w.y);
                    Vector2 step = target - m.p;
                    m.v = Vector2.Lerp(m.v, step / Mathf.Max(dt, 1e-4f), 0.35f);   // the flick, smoothed
                    m.p = target;
                }
                else if (m.finger != int.MinValue)   // let go
                {
                    m.finger = int.MinValue;
                    if (m.v.magnitude > 0.25f && m.p.magnitude > planetR + m.r)
                    {
                        m.v = Vector2.ClampMagnitude(m.v, 3f);
                        m.flying = true;
                        m.swept = 0f;
                        m.trail.Clear();
                        m.trail.emitting = true;
                    }
                    else m.back = 0.001f;   // back to its place
                }
                else if (m.flying)
                {
                    for (int s = 0; s < 4; s++)   // semi-implicit Euler, a few steps a frame
                    {
                        float h = dt / 4f, d = Mathf.Max(m.p.magnitude, 0.05f);
                        m.v += -gm * m.p / (d * d * d) * h;
                        Vector2 before = m.p;
                        m.p += m.v * h;
                        m.swept += Vector2.SignedAngle(before, m.p) * Mathf.Deg2Rad;
                    }
                    Vector3 world = origin + (Vector3)m.p;
                    bool crashed = m.p.magnitude < planetR + m.r;
                    if (crashed || Mathf.Abs(world.x - center.x) > 1.35f || world.y < center.y - 0.1f || world.y > center.y + 3.3f)
                    {
                        if (crashed) Burst(world, 14, dark, 1.2f, 0.03f);
                        if (m.stable) { stable--; Fill(count, stable / 3f); }
                        Lost(m);
                    }
                    else if (!m.stable && Mathf.Abs(m.swept) > Mathf.PI * 2.5f)
                    {
                        m.stable = true;
                        stable++;
                        Fill(count, stable / 3f);
                        Burst(world, 16, red, 1.2f, 0.03f);
                    }
                }
                else if (m.back > 0f)
                {
                    m.back += dt / 0.6f;
                    m.p = Vector2.Lerp(m.p, m.dock, Smooth(Mathf.Clamp01(m.back)));
                    if (m.back >= 1f) { m.back = 0f; m.p = m.dock; }
                }
                Place(m, origin);
            }
            yield return null;
        }

        TextOut(title);
        TextOut(hint);
        var done = Text("Lab_OrbitDone", TextY);
        for (float t = 0f; t < 3.2f; t += Time.deltaTime)   // the three go on round
        {
            planet.Rotate(Vector3.up, 12f * Time.deltaTime, Space.World);
            foreach (var m in moons)
            {
                for (int s = 0; s < 4; s++)
                {
                    float h = Time.deltaTime / 4f, d = Mathf.Max(m.p.magnitude, 0.05f);
                    m.v += -gm * m.p / (d * d * d) * h;
                    m.p += m.v * h;
                }
                Place(m, origin);
            }
            yield return null;
        }
        TextOut(done);
        yield return ShrinkAll(0.5f);
    }

    private static void Lost(Moon m)
    {
        m.flying = false;
        m.stable = false;
        m.trail.emitting = false;
        m.back = 0.001f;
        m.v = Vector2.zero;
    }

    private static void Place(Moon m, Vector3 origin)
    {
        Vector3 offset = m.t.position - m.look.bounds.center;   // the mesh may sit off its pivot
        m.t.position = origin + (Vector3)m.p + offset;
        m.t.Rotate(Vector3.up, 40f * Time.deltaTime, Space.World);
    }

    // ------------------------------------------------ 4 stallion: swipe to make it run

    private IEnumerator HorseStation()
    {
        var horse = Load("Models/Stallion/Stallion", true);
        foreach (var r in horse.GetComponentsInChildren<Renderer>())
            if (r.name.StartsWith("Icosphere")) r.enabled = false;   // a helper sphere inside the file
        Fit(horse, 1.4f, 2.2f, 0.32f);
        var tail = FindPart(horse.transform, "Tail");
        var tailLook = tail != null ? tail.GetComponent<Renderer>() : null;
        if (tailLook != null)   // facing right, turned a little toward the viewer
        {
            Vector3 toTail = tailLook.bounds.center - Middle(horse);
            toTail.y = 0f;
            horse.transform.RotateAround(Middle(horse), Vector3.up,
                Vector3.SignedAngle(toTail, Quaternion.Euler(0f, -20f, 0f) * Vector3.left, Vector3.up));
            Fit(horse, 1.4f, 2.2f, 0.32f);
        }
        var anim = horse.GetComponentInChildren<Animation>();
        string idleClip = null, trotClip = null, gallopClip = null;
        if (anim != null)
            foreach (AnimationState s in anim)
            {
                if (s.name.Contains("Chew")) idleClip = s.name;
                else if (s.name.Contains("Trot")) trotClip = s.name;
                else if (s.name.Contains("Gallop")) gallopClip = s.name;
                s.wrapMode = WrapMode.Loop;
            }
        string playing = null;
        void Play(string clip, float speed)
        {
            if (anim == null || clip == null) return;
            if (playing != clip) { anim.CrossFade(clip, 0.25f); playing = clip; }
            anim[clip].speed = speed;
        }

        var title = Text("Lab_Horse", TextY);
        var hint = Text("Hint_Horse", HintY, true);
        const float zone = 0.75f;
        var meter = Row(new Vector3(-0.8f, 2.0f, 0f), new Vector3(0.8f, 2.0f, 0f), 20, 0.05f);
        float groundY = center.y + 0.3f;
        var dashes = new List<Transform>();
        for (int i = 0; i < 14; i++)
        {
            var d = Prim(PrimitiveType.Cube, "Track", dark, false);
            d.transform.localScale = new Vector3(0.14f, 0.008f, 0.03f);
            d.transform.position = new Vector3(center.x - 1.3f + i * (2.6f / 14f), groundY, center.z - 0.25f + (i % 2) * 0.5f);
            dashes.Add(d.transform);
        }

        float energy = 0f, galloped = 0f;
        while (galloped < 4f)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            float dt = Time.deltaTime;
            foreach (var f in fingers) energy += f.delta.magnitude / Screen.height * 0.9f;
            energy = Mathf.Clamp(energy - dt * 0.35f, 0f, 1.3f);
            if (energy >= zone) galloped += dt;
            Fill(meter, energy, zone);
            Gait(energy);
            yield return null;
        }

        TextOut(title);
        TextOut(hint);
        var done = Text("Lab_HorseDone", TextY);
        Burst(Middle(horse), 30, red, 2f);
        for (float t = 0f; t < 3f; t += Time.deltaTime)
        {
            energy = Mathf.MoveTowards(energy, 1f, Time.deltaTime);
            Gait(energy);
            yield return null;
        }
        TextOut(done);
        yield return ShrinkAll(0.5f);

        void Gait(float e)
        {
            if (e < 0.2f) Play(idleClip, 1f);
            else if (e < zone) Play(trotClip ?? gallopClip, 0.7f + e);
            else Play(gallopClip ?? trotClip, 0.7f + 0.5f * e);
            float speed = e * 3.2f;   // the track runs under it
            foreach (var d in dashes)
            {
                if (d == null) continue;
                var p = d.position;
                p.x -= speed * Time.deltaTime;
                if (p.x < center.x - 1.3f) p.x += 2.6f;
                d.position = p;
            }
        }
    }

    // ------------------------------------------------ the end: the core, awake

    private IEnumerator Finale()
    {
        var core = Model("Models/Tesseract/Tesseract", 1.5f, 1.6f, 0.45f);
        int face = 0;
        foreach (var r in Parts(core))   // one face for each station
            if (face < lit && r.name.StartsWith("Cube.00") && r.name != "Cube.008") { r.sharedMaterial = red; face++; }
        var pivot = Pivot(core);
        Vector3 mid = pivot.position;
        var open = Opener(core, mid, 0.2f, 0.6f);
        Burst(mid, 50, red, 2.6f);
        var title = Text("Lab_Awake", TextY);
        var hint = Text("Hint_Awake", HintY, true);

        Vector2 turn = new Vector2(30f, 0f);
        float explode = 0f, pinch0 = -1f;
        wheelOpen = 0f;
        lastTouch = Time.time;
        float shownAt = Time.time;
        // On to the opening after a while: 8 s without a touch, or 25 s in all.
        while (Time.time - lastTouch < 8f && Time.time - shownAt < 25f && !TakeSkip())
        {
            float dt = Time.deltaTime;
            if (Held() == 1 && !fingers[0].began)
                turn = Vector2.Lerp(turn, new Vector2(-fingers[0].delta.x, fingers[0].delta.y) / Mathf.Max(dt, 1e-4f) / Screen.height * 180f, 0.5f);
            float target = Spread(ref pinch0);
            explode = Mathf.MoveTowards(explode, target, dt * 2.5f);
            open(explode);
            turn = Vector2.Lerp(turn, new Vector2(30f, 0f), dt * 0.8f);   // settles back to a slow turn
            pivot.Rotate(Vector3.up, turn.x * dt, Space.World);
            pivot.Rotate(Vector3.right, turn.y * dt, Space.World);
            yield return null;
        }
        lastTouch = Time.time;
        TextOut(title);
        TextOut(hint);
        yield return ShrinkAll(0.6f);
    }

    // ------------------------------------------------ 5 globe: where are you from?

    private const string PinsKey = "HLD_LabPins";   // every visitor's pin, kept between runs (PlayerPrefs)
    private const int MaxPins = 200;

    private IEnumerator GlobeStation()
    {
        var globe = Model("Models/Globe/Atlas", 1.45f, 1.45f, 0.5f);
        var pivot = Pivot(globe);
        Vector3 mid = pivot.position;
        float radius = GesturePlayer.GetBounds(Parts(globe)).extents.y;
        foreach (var dir in LoadPins())   // the guests before: graphite pins
            Pin(globe.transform, mid, radius, globe.transform.TransformDirection(dir), false);
        var title = Text("Lab_Globe", TextY);
        var hint = Text("Hint_Globe", HintY, true);

        float yaw = 0f, yawSpeed = 15f, tilt = 0f, doneAt = -1f;
        while (doneAt < 0f || Time.time < doneAt)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            float dt = Time.deltaTime;
            foreach (var f in fingers)
            {
                if (!f.began && !f.ended && Held() == 1)   // drag: spin and tilt, with a flick it keeps turning
                {
                    yawSpeed = Mathf.Lerp(yawSpeed, -f.delta.x / Mathf.Max(dt, 1e-4f) / Screen.height * 220f, 0.5f);
                    tilt = Mathf.Clamp(tilt + f.delta.y / Screen.height * 90f, -30f, 30f);
                }
                bool tap = f.ended && Time.time - f.since < 0.35f && (f.pos - f.start).magnitude < Screen.height * 0.03f;
                if (tap && HitSphere(f.pos, mid, radius, out Vector3 hit))
                {
                    Pin(globe.transform, mid, radius, hit - mid, true);
                    SavePin(globe.transform.InverseTransformDirection((hit - mid).normalized));
                    if (doneAt < 0f) doneAt = Time.time + 4f;   // a moment for the others to add theirs
                    else doneAt = Mathf.Max(doneAt, Time.time + 2f);
                }
            }
            yawSpeed = Mathf.Lerp(yawSpeed, 15f, dt * 0.6f);
            yaw += yawSpeed * dt;
            pivot.rotation = Quaternion.AngleAxis(tilt, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
            yield return null;
        }

        TextOut(title);
        TextOut(hint);
        var done = Text("Lab_GlobeDone", TextY);
        for (float t = 0f; t < 2.6f; t += Time.deltaTime)
        {
            yaw += 40f * Time.deltaTime;
            pivot.rotation = Quaternion.AngleAxis(tilt, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
            yield return null;
        }
        TextOut(done);
        yield return ShrinkAll(0.5f);
    }

    private bool HitSphere(Vector2 screen, Vector3 c, float r, out Vector3 hit)
    {
        var ray = cam.ScreenPointToRay(screen);
        Vector3 oc = ray.origin - c;
        float b = Vector3.Dot(oc, ray.direction), disc = b * b - (oc.sqrMagnitude - r * r);
        hit = default;
        if (disc < 0f) return false;
        hit = ray.GetPoint(-b - Mathf.Sqrt(disc));
        return true;
    }

    // A map pin standing out of the globe; a new one drops in, red, the others are graphite.
    private void Pin(Transform globe, Vector3 mid, float radius, Vector3 dir, bool fresh)
    {
        Vector3 n = dir.normalized;
        var root = new GameObject("Pin").transform;
        root.position = mid + n * radius;
        root.rotation = Quaternion.FromToRotation(Vector3.up, n);
        var stick = Prim(PrimitiveType.Cylinder, "Stick", dark);
        stick.transform.SetParent(root, false);
        stick.transform.localScale = new Vector3(0.012f, 0.06f, 0.012f);
        stick.transform.localPosition = Vector3.up * 0.06f;
        var head = Prim(PrimitiveType.Sphere, "Head", fresh ? red : dark);
        head.transform.SetParent(root, false);
        head.transform.localScale = Vector3.one * (fresh ? 0.06f : 0.045f);
        head.transform.localPosition = Vector3.up * 0.125f;
        root.SetParent(globe, true);
        if (!fresh) return;
        Vector3 rest = root.localPosition, from = rest + globe.InverseTransformVector(n * 0.5f);
        StartCoroutine(Tween(0.5f, t => { if (root != null) root.localPosition = Vector3.LerpUnclamped(from, rest, EaseOutBack(t)); }));
        StartCoroutine(Delayed(0.35f, BurstRun(mid + n * radius, 10, red, 0.8f, 0.025f)));
    }

    private static List<Vector3> LoadPins()
    {
        var list = new List<Vector3>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (string item in PlayerPrefs.GetString(PinsKey, "").Split(';'))
        {
            var p = item.Split(',');
            if (p.Length == 3 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out float x) &&
                float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out float y) &&
                float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out float z))
                list.Add(new Vector3(x, y, z));
        }
        return list;
    }

    private static void SavePin(Vector3 dir)
    {
        var list = LoadPins();
        list.Add(dir);
        if (list.Count > MaxPins) list.RemoveRange(0, list.Count - MaxPins);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        PlayerPrefs.SetString(PinsKey, string.Join(";", list.ConvertAll(v => v.x.ToString("0.###", inv) + "," + v.y.ToString("0.###", inv) + "," + v.z.ToString("0.###", inv))));
        PlayerPrefs.Save();
    }

    // ------------------------------------------------ the opening: raise the institute, cut the ribbon

    private int confettiMade;

    private IEnumerator Ceremony()
    {
        confettiMade = 0;
        var plaza = Load("Story/Plaza");
        plaza.transform.localScale *= 1.15f;
        plaza.transform.position = center + Vector3.up * 0.078f;
        var lab = Model("Story/AIRI_Lab", 2.0f, 2.2f, 0.078f);
        lab.transform.position += Vector3.forward * 0.3f;
        var floors = new List<Transform>();   // F0..F6, roof, door (make_story_assets.py lab())
        foreach (var t in lab.GetComponentsInChildren<Transform>(true))
            if ((t.name.Length == 2 && t.name[0] == 'F' && char.IsDigit(t.name[1])) || t.name == "Roof" || t.name == "Door") floors.Add(t);
        int Order(string n) => n == "Door" ? 0 : n == "Roof" ? 99 : n[1] - '0';
        floors.Sort((p, q) => Order(p.name).CompareTo(Order(q.name)));
        foreach (var f in floors) f.gameObject.SetActive(false);

        // 1: every tap raises the next storey; several fingers, several storeys
        var title = Text("Lab_Build", TextY);
        var hint = Text("Hint_Build", HintY, true);
        int next = 0;
        lastTouch = Time.time;
        while (next < floors.Count)
        {
            if (next > 0 && Idle()) { Clear(); yield break; }   // the untouched building is the resting state
            bool all = TakeSkip();
            foreach (var f in fingers)
                if ((f.began || all) && next < floors.Count) Raise(floors[next++]);
            while (all && next < floors.Count) Raise(floors[next++]);
            yield return null;
        }
        yield return new WaitForSeconds(0.8f);
        TextOut(title);
        TextOut(hint);

        // 2: the ribbon between two posts in front of the door; a swipe across cuts it
        float groundY = center.y + 0.078f, tieY = groundY + 0.5f, z = center.z - 0.55f, half = 0.78f;
        foreach (float x in new[] { -half - 0.02f, half + 0.02f })
        {
            var post = Prim(PrimitiveType.Cylinder, "Post", dark);
            post.transform.localScale = new Vector3(0.035f, (tieY - groundY + 0.04f) * 0.5f, 0.035f);
            post.transform.position = new Vector3(center.x + x, (groundY + tieY + 0.04f) * 0.5f, z);
            var top = Prim(PrimitiveType.Sphere, "Post top", red);
            top.transform.localScale = Vector3.one * 0.06f;
            top.transform.position = new Vector3(center.x + x, tieY + 0.05f, z);
        }
        const int pieces = 28;
        float length = 2f * half / pieces;
        var ribbon = new List<Transform>();
        for (int i = 0; i < pieces; i++)
        {
            var seg = Prim(PrimitiveType.Cube, "Ribbon", red);
            seg.transform.localScale = new Vector3(length * 1.08f, 0.07f, 0.006f);
            ribbon.Add(seg.transform);
        }
        var bow = new List<Transform>();
        foreach (float a in new[] { 35f, -35f, 0f })
        {
            var loop = Prim(PrimitiveType.Sphere, "Bow", red);
            loop.transform.localScale = a == 0f ? new Vector3(0.06f, 0.06f, 0.04f) : new Vector3(0.16f, 0.07f, 0.025f);
            loop.transform.rotation = Quaternion.Euler(0f, 0f, a);
            bow.Add(loop.transform);
        }
        Vector3 Point(float s, float wave) =>   // along the ribbon, 0..1, with a gentle sag and flutter
            new Vector3(center.x - half + 2f * half * s, tieY - 0.07f * (1f - (2f * s - 1f) * (2f * s - 1f)) + wave, z);
        void Lay(float time)
        {
            for (int i = 0; i < pieces; i++)
            {
                if (ribbon[i] == null) continue;
                float s0 = i / (float)pieces, s1 = (i + 1f) / pieces;
                Vector3 a = Point(s0, Mathf.Sin(time * 3f + i * 0.5f) * 0.006f), b = Point(s1, Mathf.Sin(time * 3f + (i + 1) * 0.5f) * 0.006f);
                ribbon[i].position = (a + b) * 0.5f;
                ribbon[i].rotation = Quaternion.FromToRotation(Vector3.right, b - a);
            }
            for (int i = 0; i < bow.Count; i++)
                if (bow[i] != null) bow[i].position = Point(0.5f, 0f) + new Vector3((i == 0 ? -1 : i == 1 ? 1 : 0) * 0.07f, 0f, -0.02f);
        }
        title = Text("Lab_Ribbon", TextY);
        hint = Text("Hint_Ribbon", HintY, true);
        int cut = -1;
        while (cut < 0)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) cut = pieces / 2;
            Lay(Time.time);
            foreach (var f in fingers)
            {
                if (f.began || f.delta.magnitude < Screen.height * 0.008f) continue;   // a swipe, not a touch
                Vector2 a = f.pos - f.delta, b = f.pos;
                for (int i = 0; i < pieces && cut < 0; i++)
                {
                    Vector2 p = cam.WorldToScreenPoint(ribbon[i].position - ribbon[i].right * length * 0.5f);
                    Vector2 q = cam.WorldToScreenPoint(ribbon[i].position + ribbon[i].right * length * 0.5f);
                    if (Crosses(a, b, p, q)) cut = i;
                }
            }
            yield return null;
        }

        // Cut: the two halves swing down from their posts, the bow drops, confetti rains.
        Vector3 cutAt = ribbon[cut].position;
        Kill(ribbon[cut].gameObject);
        TextOut(title);
        TextOut(hint);
        Burst(cutAt, 24, red, 1.6f, 0.03f);
        StartCoroutine(Confetti(Vector3.zero, 160, true));
        StartCoroutine(Fall(bow, groundY));
        StartCoroutine(Hang(ribbon, 0, cut, new Vector3(center.x - half, tieY, z), 1f, length));
        StartCoroutine(Hang(ribbon, cut + 1, pieces, new Vector3(center.x + half, tieY, z), -1f, length));
        yield return new WaitForSeconds(0.6f);
        var welcome = Text("Phrase_Welcome", TextY);
        hint = Text("Hint_Confetti", HintY, true);

        // 3: the celebration; every tap throws more confetti; on to the menu 6 s after the last touch (20 s at most)
        lastTouch = Time.time;
        float since = Time.time;
        while (Time.time - lastTouch < 6f && Time.time - since < 20f && !TakeSkip())
        {
            foreach (var f in fingers)
                if (f.began) StartCoroutine(Confetti(OnPlane(f.pos, center.z - 0.6f), 40, false));
            yield return null;
        }
        TextOut(welcome);
        TextOut(hint);
        yield return ShrinkAll(0.6f);
    }

    // One storey: it appears a little above its place and settles down into it, widening as it lands.
    private void Raise(Transform part)
    {
        part.gameObject.SetActive(true);
        Vector3 rest = part.localPosition, scale = part.localScale;
        Vector3 lift = part.parent.InverseTransformVector(Vector3.up * 0.45f);
        StartCoroutine(Tween(0.55f, t =>
        {
            if (part == null) return;
            part.localPosition = Vector3.LerpUnclamped(rest + lift, rest, EaseOutBack(t));
            float plan = Mathf.Lerp(0.8f, 1f, Smooth(t));
            part.localScale = Vector3.Scale(scale, new Vector3(plan, 1f, plan));
        }));
    }

    private static bool Crosses(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
    {
        float Side(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
        return Side(a, b, p) * Side(a, b, q) < 0f && Side(p, q, a) * Side(p, q, b) < 0f;
    }

    // Ribbon pieces [from, to) swing down from `anchor` (side +1: hanging off the left post, -1: the right one).
    private IEnumerator Hang(List<Transform> ribbon, int from, int to, Vector3 anchor, float side, float length)
    {
        int count = to - from;
        if (count <= 0) yield break;
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            for (int k = 0; k < count; k++)
            {
                int i = side > 0f ? from + k : to - 1 - k;   // k = 0 is the piece at the post
                if (ribbon[i] == null) continue;
                float lag = Mathf.Max(0f, t - k * 0.012f);
                // from level to hanging, overshooting and settling like a pendulum
                float angle = -88f * (1f - Mathf.Exp(-3.5f * lag) * Mathf.Cos(7f * lag)) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle) * side, Mathf.Sin(angle), 0f);
                ribbon[i].position = anchor + dir * length * (k + 0.5f);
                ribbon[i].rotation = Quaternion.FromToRotation(Vector3.right, dir * side);
            }
            yield return null;
        }
    }

    private IEnumerator Fall(List<Transform> parts, float groundY)
    {
        float v = 0f;
        for (float t = 0f; t < 1.5f; t += Time.deltaTime)
        {
            v -= 3f * Time.deltaTime;
            foreach (var p in parts)
            {
                if (p == null) continue;
                var pos = p.position;
                pos.y = Mathf.Max(groundY + 0.03f, pos.y + v * Time.deltaTime);
                p.position = pos;
                p.Rotate(Vector3.forward, 180f * Time.deltaTime);
            }
            yield return null;
        }
    }

    // Paper squares in the lab's colours: a rain from above the alcove, or a handful thrown up from a point.
    // They flutter down and stay where they land on the lawn.
    private IEnumerator Confetti(Vector3 at, int n, bool rain)
    {
        n = Mathf.Min(n, 700 - confettiMade);   // they stay on the lawn: keep the count sane
        confettiMade += Mathf.Max(0, n);
        var bits = new List<(Transform t, Vector3 v, Vector3 axis, float phase)>();
        Material[] colours = { red, red, dark, light };
        for (int i = 0; i < n; i++)
        {
            var c = Prim(PrimitiveType.Cube, "Confetti", colours[i % colours.Length], false);
            c.transform.localScale = new Vector3(0.045f, 0.028f, 0.003f);
            c.transform.rotation = UnityEngine.Random.rotation;
            Vector3 start, v;
            if (rain)
            {
                start = center + new Vector3(UnityEngine.Random.Range(-1.2f, 1.2f), 3.4f + UnityEngine.Random.Range(0f, 1.2f), UnityEngine.Random.Range(-0.7f, 0.3f));
                v = Vector3.down * UnityEngine.Random.Range(0.3f, 0.6f);
            }
            else
            {
                start = at;
                v = (Vector3.up + UnityEngine.Random.insideUnitSphere * 0.8f).normalized * UnityEngine.Random.Range(1.2f, 2.4f);
            }
            c.transform.position = start;
            bits.Add((c.transform, v, UnityEngine.Random.onUnitSphere, UnityEngine.Random.value * 6f));
        }
        float ground = center.y + 0.085f;
        for (float t = 0f; t < 9f && bits.Count > 0; t += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var (b, v, axis, phase) = bits[i];
                if (b == null) { bits.RemoveAt(i); continue; }
                v.y = Mathf.Max(v.y - 3f * dt, -0.55f);   // paper: falls slowly
                v.x += Mathf.Sin(Time.time * 3f + phase) * 0.8f * dt;
                v.x *= 1f - dt;
                b.position += v * dt;
                b.Rotate(axis, 360f * dt, Space.World);
                if (b.position.y <= ground)
                {
                    b.position = new Vector3(b.position.x, ground, b.position.z);
                    b.rotation = Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f);   // lying flat
                    bits.RemoveAt(i);
                    continue;
                }
                bits[i] = (b, v, axis, phase);
            }
            yield return null;
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    // ------------------------------------------------ the menu: play the lab, or AIRI research

    private IEnumerator Menu(Action<bool> chosen)
    {
        var title = Text("Lab_Menu", TextY);
        var play = Model("Models/Tesseract/Tesseract", 0.9f, 0.9f, 0.95f);
        play.transform.position += Vector3.left * 0.6f;
        var research = Model("Story/RobotArm", 1.05f, 0.95f, 0.85f);
        research.transform.position += Vector3.right * 0.6f;
        var playPivot = Pivot(play);
        var researchPivot = Pivot(research);
        var playLabel = Text("Hint_Play", 0.62f, true, -0.6f, 1.05f);
        var researchLabel = Text("Hint_Research", 0.62f, true, 0.6f, 1.05f);

        lastTouch = Time.time;
        int pick = -1;
        while (pick < 0)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) pick = 0;
            float bob = Mathf.Sin(Time.time * 2f) * 0.04f;
            playPivot.Rotate(Vector3.up, 30f * Time.deltaTime, Space.World);
            researchPivot.Rotate(Vector3.up, -20f * Time.deltaTime, Space.World);
            playPivot.localScale = Vector3.one * (1f + bob);
            researchPivot.localScale = Vector3.one * (1f - bob);
            foreach (var f in fingers)   // a tap on either half of the display
                if (f.ended && Time.time - f.since < 0.5f && (f.pos - f.start).magnitude < Screen.height * 0.05f)
                    pick = f.pos.x < Screen.width * 0.5f ? 0 : 1;
            yield return null;
        }
        Transform picked = pick == 0 ? playPivot : researchPivot, other = pick == 0 ? researchPivot : playPivot;
        Burst(picked.position, 40, red, 2f);
        TextOut(title);
        TextOut(playLabel);
        TextOut(researchLabel);
        StartCoroutine(Shrink(other.gameObject, 0.35f));
        yield return Tween(0.45f, t => { if (picked != null) picked.localScale = Vector3.one * (1f + 0.3f * Mathf.Sin(t * Mathf.PI)); });
        yield return ShrinkAll(0.35f);
        chosen(pick == 1);
        lastTouch = Time.time;
    }

    // ------------------------------------------------ research 1: robotics, the arm puts the box on the pad

    private IEnumerator ArmStation()
    {
        var arm = Model("Story/RobotArm", 1.35f, 1.4f, 0.15f);
        Transform Part(string n) => FindPart(arm.transform, n);
        Transform turret = Part("Turret"), upper = Part("UpperArm"), fore = Part("Forearm"), wrist = Part("Wrist"),
                  fingerL = Part("FingerL"), fingerR = Part("FingerR"), crate = Part("Crate"), basePart = Part("Base");
        var title = Text("Dept_Robotics", TextY);
        var hint = Text("Hint_Arm", HintY, true);
        if (turret == null || upper == null || fore == null || wrist == null || crate == null) { yield return Done(title, hint); yield break; }
        // The file keeps every part flat under its root (make_story_assets.py); chain them, as StoryDirector does.
        turret.SetParent(basePart != null ? basePart : turret.parent, true);
        upper.SetParent(turret, true);
        fore.SetParent(upper, true);
        wrist.SetParent(fore, true);
        if (fingerL != null) fingerL.SetParent(wrist, true);
        if (fingerR != null) fingerR.SetParent(wrist, true);
        var restT = turret.localRotation; var restU = upper.localRotation; var restF = fore.localRotation;
        Vector3 yawAxis = turret.parent.InverseTransformDirection(Vector3.up);
        Vector3 pitchU = upper.parent.InverseTransformDirection(Vector3.right), pitchF = fore.parent.InverseTransformDirection(Vector3.right);
        float Toward(Transform joint, Vector3 axis, Quaternion rest)   // which way bends the hand down to the box
        {
            joint.localRotation = Quaternion.AngleAxis(10f, axis) * rest;
            float plus = (wrist.position - crate.position).sqrMagnitude;
            joint.localRotation = Quaternion.AngleAxis(-10f, axis) * rest;
            float minus = (wrist.position - crate.position).sqrMagnitude;
            joint.localRotation = rest;
            return plus < minus ? 1f : -1f;
        }
        float su = Toward(upper, pitchU, restU), sf = Toward(fore, pitchF, restF);
        Vector3 lRest = fingerL != null ? fingerL.localPosition : Vector3.zero, rRest = fingerR != null ? fingerR.localPosition : Vector3.zero;
        Vector3 grip = fingerL != null && fingerR != null ? (fingerR.localPosition - fingerL.localPosition) * 0.18f : Vector3.zero;
        void Pose(float yaw, float reach, float close)   // reach 0 = up, 1 = down at the box (the spot's 28 / 22 degrees)
        {
            turret.localRotation = Quaternion.AngleAxis(yaw, yawAxis) * restT;
            upper.localRotation = Quaternion.AngleAxis(su * 28f * reach, pitchU) * restU;
            fore.localRotation = Quaternion.AngleAxis(sf * 22f * reach, pitchF) * restF;
            if (fingerL != null) fingerL.localPosition = lRest + grip * close;
            if (fingerR != null) fingerR.localPosition = rRest - grip * close;
        }
        // The pad: where the hand comes down when the arm is swung round (as in the spot), on the floor.
        Pose(110f, 0.75f, 0f);
        Vector3 padAt = new Vector3(wrist.position.x, center.y + 0.15f, wrist.position.z);
        Pose(0f, 1f, 0f);
        float reachGrip = Vector3.Distance(wrist.position, crate.position) + 0.12f;
        Pose(0f, 0f, 0f);
        var pad = Prim(PrimitiveType.Cylinder, "Pad", red);
        pad.transform.localScale = new Vector3(0.3f, 0.006f, 0.3f);
        pad.transform.position = padAt;

        float yawNow = 0f, reachNow = 0f, close = 0f;
        bool holding = false;
        while (true)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            if (Held() == 1)
            {
                var f = fingers[0];
                yawNow = Mathf.Clamp(yawNow - f.delta.x / Screen.height * 220f, -170f, 170f);
                reachNow = Mathf.Clamp01(reachNow - f.delta.y / Screen.height * 2.5f);   // drag down: the arm reaches down
            }
            close = Mathf.MoveTowards(close, holding ? 1f : 0f, Time.deltaTime * 4f);
            Pose(yawNow, reachNow, close);
            if (!holding && reachNow > 0.75f && Vector3.Distance(wrist.position, crate.position) < reachGrip)
            {
                holding = true;
                crate.SetParent(wrist, true);
                Burst(crate.position, 10, dark, 0.7f, 0.025f);
            }
            Vector2 flat = new Vector2(crate.position.x - padAt.x, crate.position.z - padAt.z);
            if (holding && reachNow > 0.5f && flat.magnitude < 0.2f)
            {
                crate.SetParent(arm.transform, true);
                var parts = crate.GetComponentsInChildren<Renderer>();
                if (parts.Length > 0)
                {
                    Bounds b = GesturePlayer.GetBounds(parts);
                    crate.position += new Vector3(padAt.x - b.center.x, padAt.y + 0.005f - b.min.y, padAt.z - b.center.z);
                }
                break;
            }
            yield return null;
        }
        Burst(padAt + Vector3.up * 0.1f, 30, red, 1.6f);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            close = Mathf.MoveTowards(close, 0f, Time.deltaTime * 4f);
            reachNow = Mathf.MoveTowards(reachNow, 0f, Time.deltaTime * 2f);
            Pose(yawNow, reachNow, close);
            yield return null;
        }
        yield return Done(title, hint);
    }

    // Swaps the headline and hint for "Well done", holds it, and clears the station.
    private IEnumerator Done(GameObject title, GameObject hint)
    {
        TextOut(title);
        TextOut(hint);
        var done = Text("Lab_Done", TextY);
        yield return new WaitForSeconds(2.2f);
        TextOut(done);
        yield return ShrinkAll(0.5f);
    }

    // ------------------------------------------------ research 2: autonomous systems, fly the drone through the rings

    private IEnumerator DroneStation()
    {
        var drone = Model("Story/Drone", 0.32f, 0.75f, 1.2f);
        var body = Pivot(drone);
        var rotors = new List<Transform>();
        foreach (var t in drone.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Rotor")) rotors.Add(t);
        var title = Text("Dept_Autonomous", TextY);
        var hint = Text("Hint_Drone", HintY, true);

        Vector2[] spots = { new Vector2(-0.6f, 1.85f), new Vector2(0.62f, 1.25f), new Vector2(-0.35f, 0.55f) };
        var rings = new List<GameObject>();
        foreach (var p in spots)
        {
            var ring = Load("Story/Ring");
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f) * ring.transform.rotation;   // the flat ring stood up to face the viewer
            ring.transform.localScale = Vector3.one * 0.26f;
            ring.transform.position = new Vector3(center.x + p.x, center.y + p.y, center.z);
            foreach (var r in ring.GetComponentsInChildren<Renderer>()) r.sharedMaterial = light;
            rings.Add(ring);
        }
        int next = 0;
        Vector3 pos = body.position, vel = Vector3.zero;
        while (next < rings.Count)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            float dt = Time.deltaTime;
            for (int i = 0; i < rings.Count; i++)
                foreach (var r in rings[i].GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = i < next ? dark : i == next ? red : light;
            rings[next].transform.localScale = Vector3.one * 0.26f * (1f + 0.06f * Mathf.Sin(Time.time * 6f));

            Vector3 target = pos;
            if (Held() >= 1) target = OnPlane(fingers[0].pos, center.z) + Vector3.up * 0.05f;
            target.x = Mathf.Clamp(target.x, center.x - 1.1f, center.x + 1.1f);
            target.y = Mathf.Clamp(target.y, center.y + 0.3f, center.y + 2.4f);
            Vector3 want = (target - pos) * 4f;
            vel = Vector3.Lerp(vel, want, dt * 5f);
            pos += vel * dt + Vector3.up * Mathf.Sin(Time.time * 2.6f) * 0.0015f;
            body.SetPositionAndRotation(pos, Quaternion.Euler(Mathf.Clamp(vel.y * 6f, -15f, 15f), 0f, Mathf.Clamp(-vel.x * 12f, -25f, 25f)));
            foreach (var r in rotors) if (r != null) r.Rotate(body.up, 1400f * dt, Space.World);

            Vector3 c = rings[next].transform.position;
            if (new Vector2(pos.x - c.x, pos.y - c.y).magnitude < 0.17f)
            {
                Burst(c, 18, red, 1.3f, 0.03f);
                next++;
            }
            yield return null;
        }
        yield return Done(title, hint);
    }

    // ------------------------------------------------ research 3: data labelling, tap each object to label it

    private IEnumerator LabelStation()
    {
        var things = Model("Story/Labelling", 0.85f, 2.0f, 0.75f);
        var pivot = Pivot(things);
        var boxes = new List<Transform>();
        var tags = new List<Transform>();
        for (int i = 0; i < 3; i++)
        {
            var box = FindPart(things.transform, "Box" + i);
            var tag = FindPart(things.transform, "Tag" + i);
            if (box == null) continue;
            box.gameObject.SetActive(false);
            if (tag != null) tag.gameObject.SetActive(false);
            boxes.Add(box);
            tags.Add(tag);
        }
        var title = Text("Dept_Labelling", TextY);
        var hint = Text("Hint_Label", HintY, true);
        int labelled = 0;
        float angle = 0f;
        while (labelled < boxes.Count)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            angle += Time.deltaTime;
            pivot.rotation = Quaternion.Euler(0f, Mathf.Sin(angle * 0.7f) * 28f, 0f);   // they turn, so the 3D reads
            foreach (var f in fingers)
            {
                if (!f.began) continue;
                int best = -1;
                float bestD = Screen.height * 0.12f;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].gameObject.activeSelf) continue;
                    float d = Vector2.Distance(f.pos, cam.WorldToScreenPoint(boxes[i].position));
                    if (d < bestD) { best = i; bestD = d; }
                }
                if (best < 0) continue;
                labelled++;
                foreach (var t in new[] { boxes[best], tags[best] })
                {
                    if (t == null) continue;
                    t.gameObject.SetActive(true);
                    Vector3 full = t.localScale;
                    StartCoroutine(Tween(0.35f, k => { if (t != null) t.localScale = full * Mathf.Max(0.001f, EaseOutBack(k)); }));
                }
                Burst(boxes[best].position, 14, red, 1f, 0.03f);
            }
            yield return null;
        }
        yield return new WaitForSeconds(0.6f);
        yield return Done(title, hint);
    }

    // ------------------------------------------------ research 4: cybersecurity, tap the red attacks before they hit

    private IEnumerator CyberStation()
    {
        var padlock = Model("Story/Padlock", 0.75f, 0.8f, Mid - 0.38f);
        var pivot = Pivot(padlock);
        Vector3 mid = pivot.position;
        var title = Text("Dept_Cyber", TextY);
        var hint = Text("Hint_Cyber", HintY, true);
        var shield = Arc(mid, 0.6f, 30, 0.04f, 90f, 360f);
        Fill(shield, 0f);

        const int need = 12;
        var packets = new List<(Transform t, bool attack, float speed)>();
        int blocked = 0;
        float nextPacket = 0f, shake = 0f, start = Time.time;
        while (blocked < need)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            float dt = Time.deltaTime;
            if (Time.time >= nextPacket)   // traffic from all round, faster as it goes on
            {
                nextPacket = Time.time + Mathf.Lerp(0.9f, 0.45f, (Time.time - start) / 40f);
                bool attack = UnityEngine.Random.value < 0.65f;
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 from = mid + new Vector3(Mathf.Cos(a) * 1.15f, Mathf.Sin(a) * 1.3f, 0f);
                from.y = Mathf.Clamp(from.y, center.y + 0.2f, center.y + 2.5f);
                var p = Prim(attack ? PrimitiveType.Sphere : PrimitiveType.Cube, attack ? "Attack" : "Data", attack ? red : dark, false);
                p.transform.position = from;
                p.transform.localScale = Vector3.one * (attack ? 0.1f : 0.07f);
                packets.Add((p.transform, attack, UnityEngine.Random.Range(0.3f, 0.5f)));
            }
            foreach (var f in fingers)
            {
                if (!f.began) continue;
                int best = -1;
                float bestD = Screen.height * 0.07f;
                for (int i = 0; i < packets.Count; i++)
                {
                    if (packets[i].t == null) continue;
                    float d = Vector2.Distance(f.pos, cam.WorldToScreenPoint(packets[i].t.position));
                    if (d < bestD) { best = i; bestD = d; }
                }
                if (best < 0) continue;
                var (t, attack, _) = packets[best];
                Burst(t.position, attack ? 12 : 6, attack ? red : dark, 1f, 0.025f);
                if (attack) blocked++;
                else { blocked = Mathf.Max(0, blocked - 1); shake = 1f; }   // that was good traffic
                Kill(t.gameObject);
                packets.RemoveAt(best);
                Fill(shield, blocked / (float)need);
            }
            for (int i = packets.Count - 1; i >= 0; i--)
            {
                var (t, attack, speed) = packets[i];
                if (t == null) { packets.RemoveAt(i); continue; }
                t.position = Vector3.MoveTowards(t.position, mid, speed * dt);
                t.Rotate(Vector3.one, 90f * dt);
                if (Vector3.Distance(t.position, mid) < 0.3f)
                {
                    if (attack) { shake = 1f; blocked = Mathf.Max(0, blocked - 2); Burst(t.position, 10, dark, 0.9f, 0.025f); Fill(shield, blocked / (float)need); }
                    Kill(t.gameObject);
                    packets.RemoveAt(i);
                }
            }
            shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
            pivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 50f) * 10f * shake);
            yield return null;
        }
        foreach (var (t, _, _) in packets) if (t != null) Burst(t.position, 4, light, 0.6f, 0.02f);
        Burst(mid, 30, red, 1.8f);
        yield return Done(title, hint);
    }

    // ------------------------------------------------ research 5: 5G-SHARE, link the mast to each campus

    private IEnumerator FiveGStation()
    {
        var tower = Model("Story/Tower5G", 1.65f, 0.9f, 0f);
        tower.transform.position += Vector3.forward * 0.2f;
        Bounds tb = GesturePlayer.GetBounds(Parts(tower));
        Vector3 top = new Vector3(tb.center.x, tb.max.y - 0.1f, tb.center.z);
        Vector2[] spots = { new Vector2(-0.92f, -0.45f), new Vector2(0.92f, -0.45f), new Vector2(-0.72f, 0.75f), new Vector2(0.72f, 0.75f) };
        var hubs = new List<Vector3>();
        foreach (var s in spots)
        {
            var hub = Model("Story/Hub", 0.38f, 0.5f, 0f);
            hub.transform.position += new Vector3(s.x, 0f, s.y);
            Bounds hb = GesturePlayer.GetBounds(Parts(hub));
            hubs.Add(new Vector3(hb.center.x, hb.max.y, hb.center.z));
        }
        var title = Text("Phrase_5G", TextY);
        var hint = Text("Hint_5G", HintY, true);

        var linked = new bool[hubs.Count];
        int links = 0, dragging = int.MinValue;
        LineRenderer cable = null;
        while (links < hubs.Count)
        {
            if (Idle()) { Clear(); yield break; }
            if (TakeSkip()) break;
            Vector2 mast = cam.WorldToScreenPoint(top), mastFoot = cam.WorldToScreenPoint(new Vector3(top.x, center.y, top.z));
            foreach (var f in fingers)
            {
                if (f.began && dragging == int.MinValue && Mathf.Abs(f.pos.x - mast.x) < Screen.height * 0.08f && f.pos.y > mastFoot.y - 20f && f.pos.y < mast.y + Screen.height * 0.06f)
                {
                    dragging = f.id;
                    cable = Line(red, 0.014f);
                }
                if (f.id != dragging || cable == null) continue;
                Vector3 end = OnPlane(f.pos, top.z);
                SetArc(cable, top, end, 0.15f);
                if (!f.ended) continue;
                int best = -1;
                float bestD = Screen.height * 0.12f;
                for (int i = 0; i < hubs.Count; i++)
                {
                    if (linked[i]) continue;
                    float d = Vector2.Distance(f.pos, cam.WorldToScreenPoint(hubs[i]));
                    if (d < bestD) { best = i; bestD = d; }
                }
                if (best >= 0)
                {
                    linked[best] = true;
                    links++;
                    SetArc(cable, top, hubs[best], 0.35f);
                    StartCoroutine(Packets(top, hubs[best]));
                    Burst(hubs[best], 16, red, 1.1f, 0.03f);
                }
                else Kill(cable.gameObject);
                cable = null;
                dragging = int.MinValue;
            }
            if (dragging != int.MinValue && !fingerById.ContainsKey(dragging)) { if (cable != null) Kill(cable.gameObject); cable = null; dragging = int.MinValue; }
            yield return null;
        }
        for (int w = 0; w < 3; w++)   // the network is up: waves from the mast
        {
            var ring = Load("Story/Ring");
            ring.transform.position = new Vector3(top.x, center.y + 0.02f, top.z);
            StartCoroutine(Tween(1.2f, t => { if (ring != null) ring.transform.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Lerp(0.15f, 1.2f, t) * (1f - t * t)); }));
            yield return new WaitForSeconds(0.35f);
        }
        yield return Done(title, hint);
    }

    private LineRenderer Line(Material material, float width)
    {
        var go = new GameObject("Cable");
        Track(go);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.positionCount = 24;
        line.generateLightingData = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }

    // A bowed arc from a to b, rising `lift` in the middle.
    private static void SetArc(LineRenderer line, Vector3 a, Vector3 b, float lift)
    {
        for (int i = 0; i < line.positionCount; i++)
        {
            float t = i / (line.positionCount - 1f);
            line.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.up * lift * 4f * t * (1f - t));
        }
    }

    // Data running along a link, for the rest of the station.
    private IEnumerator Packets(Vector3 a, Vector3 b)
    {
        var dots = new List<Transform>();
        for (int i = 0; i < 3; i++)
        {
            var d = Prim(PrimitiveType.Sphere, "Packet", dark, false);
            d.transform.localScale = Vector3.one * 0.04f;
            dots.Add(d.transform);
        }
        for (float time = 0f; dots.Exists(d => d != null); time += Time.deltaTime)
        {
            for (int i = 0; i < dots.Count; i++)
            {
                if (dots[i] == null) continue;
                float t = Mathf.Repeat(time * 0.7f + i / 3f, 1f);
                dots[i].position = Vector3.Lerp(a, b, t) + Vector3.up * 0.35f * 4f * t * (1f - t);
            }
            yield return null;
        }
    }

    // ------------------------------------------------ the research path's end: AIRI and UTCN, join us

    private IEnumerator ResearchEnd()
    {
        var airi = Model("Models/AIRI_Intro/AIRI_Intro", 0.7f, 1.0f, 1.0f);
        airi.transform.position += Vector3.left * 0.55f;
        var utcn = Model("Models/UTCN/UTCN", 0.7f, 1.0f, 1.0f);
        utcn.transform.position += Vector3.right * 0.55f;
        var title = Text("Phrase_JoinUs", TextY);
        StartCoroutine(Confetti(Vector3.zero, 120, true));
        lastTouch = Time.time;
        float since = Time.time;
        while (Time.time - lastTouch < 5f && Time.time - since < 15f && !TakeSkip())
        {
            foreach (var f in fingers)
                if (f.began) StartCoroutine(Confetti(OnPlane(f.pos, center.z - 0.6f), 30, false));
            yield return null;
        }
        TextOut(title);
        yield return ShrinkAll(0.6f);
    }

    // ---------------------------------------------------------------- building blocks

    private void Track(GameObject go) { if (go != null) spawned.Add(go); }

    private void Kill(GameObject go)
    {
        if (go == null) return;
        spawned.Remove(go);
        letters.Remove(go);
        Destroy(go);
    }

    private void Clear()
    {
        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();
        letters.Clear();
        keep.Clear();
        baseSize.Clear();
    }

    private GameObject Load(string path, bool keepAnimation = false)
    {
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"PlayLab: nothing at Resources/{path}");
            var missing = new GameObject("missing " + path);
            missing.transform.position = center;
            Track(missing);
            return missing;
        }
        var go = Instantiate(prefab, center, Quaternion.Euler(0f, 180f, 0f) * prefab.transform.rotation);
        if (!keepAnimation)   // the lab moves them by touch, not by their own clips
        {
            foreach (var a in go.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var a in go.GetComponentsInChildren<Animation>()) a.enabled = false;
        }
        Track(go);
        return go;
    }

    private GameObject Model(string path, float height, float width, float bottom)
    {
        var go = Load(path);
        Fit(go, height, width, bottom);
        return go;
    }

    // Scales to fit height x width, stands it `bottom` above the stage, centred.
    private void Fit(GameObject go, float height, float width, float bottom)
    {
        var parts = Parts(go);
        if (parts.Length == 0) return;
        Bounds b = GesturePlayer.GetBounds(parts);
        go.transform.localScale *= Mathf.Min(height / b.size.y, width / Mathf.Max(b.size.x, b.size.z));
        b = GesturePlayer.GetBounds(parts);
        go.transform.position += new Vector3(center.x, center.y + bottom, center.z) - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    // A parent at the model's middle, to turn and scale it in place.
    private Transform Pivot(GameObject go)
    {
        var pivot = new GameObject(go.name + " pivot").transform;
        pivot.position = Middle(go);
        go.transform.SetParent(pivot, true);
        Track(pivot.gameObject);
        return pivot;
    }

    // Moves the parts out from the middle (0 = closed, 1 = open), each by how far out it already sits.
    private Action<float> Opener(GameObject go, Vector3 mid, float min, float stretch)
    {
        var parts = Parts(go);
        var start = new Vector3[parts.Length];
        var away = new Vector3[parts.Length];
        float size = GesturePlayer.GetBounds(parts).size.magnitude;
        for (int i = 0; i < parts.Length; i++)
        {
            var t = parts[i].transform;
            start[i] = t.localPosition;
            Vector3 out_ = parts[i].bounds.center - mid;
            Vector3 dir = out_.sqrMagnitude > 1e-8f ? out_.normalized : Vector3.up;
            Vector3 world = dir * size * (min + stretch * out_.magnitude / size);
            away[i] = t.parent != null ? t.parent.InverseTransformVector(world) : world;
        }
        return e =>
        {
            float k = Smooth(e);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) parts[i].transform.localPosition = start[i] + away[i] * k;
        };
    }

    private static Transform FindPart(Transform root, string part)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0 && t.GetComponent<Renderer>() != null) return t;
        return null;
    }

    private static Renderer[] Parts(GameObject go) => go == null ? new Renderer[0]
        : Array.FindAll(go.GetComponentsInChildren<Renderer>(), r => (r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled);

    private static Vector3 Middle(GameObject go)
    {
        var parts = Parts(go);
        return parts.Length > 0 ? GesturePlayer.GetBounds(parts).center : go.transform.position;
    }

    private Vector3 OnPlane(Vector2 screen, float z)
    {
        var ray = cam.ScreenPointToRay(screen);
        return new Plane(Vector3.forward, new Vector3(0f, 0f, z)).Raycast(ray, out float d) ? ray.GetPoint(d) : ray.GetPoint(10f);
    }

    private GameObject Prim(PrimitiveType type, string name, Material material, bool shadow = true)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());   // or the floor raycast would land on it
        go.name = name;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        if (!shadow) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Track(go);
        return go;
    }

    // Beads on an arc in the plane facing the viewer: the lab's dials. Angles in degrees, 0 = right, counter-clockwise.
    private List<Renderer> Arc(Vector3 at, float radius, int n, float size, float from, float span)
    {
        var list = new List<Renderer>();
        bool closed = Mathf.Abs(span) >= 360f;
        for (int i = 0; i < n; i++)
        {
            float a = (from + span * i / (closed ? n : n - 1)) * Mathf.Deg2Rad;
            var b = Prim(PrimitiveType.Sphere, "Bead", light);
            b.transform.position = at + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            b.transform.localScale = Vector3.one * size;
            list.Add(b.GetComponent<Renderer>());
        }
        return list;
    }

    // Beads in a row; ends relative to the stage centre.
    private List<Renderer> Row(Vector3 left, Vector3 right, int n, float size)
    {
        var list = new List<Renderer>();
        for (int i = 0; i < n; i++)
        {
            var b = Prim(PrimitiveType.Sphere, "Bead", light);
            b.transform.position = center + Vector3.Lerp(left, right, n > 1 ? i / (n - 1f) : 0.5f) + Vector3.back * 0.3f;
            b.transform.localScale = Vector3.one * size;
            list.Add(b.GetComponent<Renderer>());
        }
        return list;
    }

    // The first `share` of the beads lit: graphite, red from `redFrom` on.
    private void Fill(List<Renderer> beads, float share, float redFrom = 0f)
    {
        for (int i = 0; i < beads.Count; i++)
        {
            if (beads[i] == null) continue;
            float at = (i + 0.5f) / beads.Count;
            beads[i].sharedMaterial = at > share ? light : at >= redFrom ? red : dark;
        }
    }

    private void Swell(List<Renderer> beads, float factor)
    {
        foreach (var b in beads)
        {
            if (b == null) continue;
            if (!baseSize.TryGetValue(b, out float s)) baseSize[b] = s = b.transform.localScale.x;
            b.transform.localScale = Vector3.one * s * factor;
        }
    }

    private readonly Dictionary<Renderer, float> baseSize = new Dictionary<Renderer, float>();

    private void Burst(Vector3 at, int n, Material material, float speed, float size = 0.045f) => StartCoroutine(BurstRun(at, n, material, speed, size));

    private IEnumerator BurstRun(Vector3 at, int n, Material material, float speed, float size)
    {
        var bits = new List<(Transform t, Vector3 v)>();
        for (int i = 0; i < n; i++)
        {
            var b = Prim(PrimitiveType.Sphere, "Spark", i % 3 == 0 ? dark : material, false);
            b.transform.position = at;
            b.transform.localScale = Vector3.one * size;
            bits.Add((b.transform, UnityEngine.Random.onUnitSphere * speed * UnityEngine.Random.Range(0.4f, 1f)));
        }
        for (float t = 0f; t < 0.9f; t += Time.deltaTime)
        {
            for (int i = 0; i < bits.Count; i++)
            {
                var (b, v) = bits[i];
                if (b == null) continue;
                v += Vector3.down * 2.5f * Time.deltaTime;
                b.position += v * Time.deltaTime;
                b.localScale = Vector3.one * size * (1f - t / 0.9f);
                bits[i] = (b, v);
            }
            yield return null;
        }
        foreach (var (b, _) in bits) if (b != null) Kill(b.gameObject);
    }

    private IEnumerator Shrink(GameObject go, float seconds)
    {
        if (go == null) yield break;
        Vector3 full = go.transform.localScale;
        for (float t = 0f; t < seconds && go != null; t += Time.deltaTime)
        {
            go.transform.localScale = full * Mathf.Max(0.001f, 1f - Smooth(t / seconds));
            yield return null;
        }
        Kill(go);
    }

    // Everything of the station goes, except the progress dots.
    private IEnumerator ShrinkAll(float seconds)
    {
        var all = new List<GameObject>();
        foreach (var go in spawned)
            if (go != null && go.transform.parent == null && !keep.Contains(go)) all.Add(go);
        var full = new List<Vector3>();
        foreach (var go in all) full.Add(go.transform.localScale);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null) all[i].transform.localScale = full[i] * Mathf.Max(0.001f, 1f - Smooth(t / seconds));
            yield return null;
        }
        foreach (var go in all) Kill(go);
    }

    // ---------------------------------------------------------------- 3D headlines

    private GameObject Text(string name, float y, bool hint = false, float x = 0f, float width = TextWidth)
    {
        float size = name.StartsWith("Hint_") ? 0.15f : name.StartsWith("Dept_") ? 0.22f : 0.38f;
        var go = Load("Story/" + name);
        var parts = Parts(go);
        foreach (var r in parts) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // only objects throw shadows
        if (parts.Length == 0) return go;
        Bounds b = GesturePlayer.GetBounds(parts);
        go.transform.localScale *= Mathf.Min(size, width / b.size.x, (hint ? 0.36f : 0.62f) / b.size.y);
        b = GesturePlayer.GetBounds(parts);
        go.transform.position += new Vector3(center.x + x, center.y + y, center.z - TextForward) - b.center;

        var list = new List<(Transform, Vector3, Quaternion, Vector3)>();
        foreach (var t in go.GetComponentsInChildren<Transform>())
            if (t.name.Length == 3 && t.name[0] == 'L' && t.GetComponent<Renderer>() != null)
                list.Add((t, t.localPosition, t.localRotation, t.localScale));
        list.Sort((p, q) => string.CompareOrdinal(p.Item1.name, q.Item1.name));
        letters[go] = list;
        for (int i = 0; i < list.Count; i++)
        {
            var (letter, pos, rot, scale) = list[i];
            letter.localScale = Vector3.one * 0.001f;
            StartCoroutine(Delayed(i * 0.025f, Tween(0.55f, t =>
            {
                if (letter == null) return;
                float e = Smooth(t);
                letter.localPosition = pos + Vector3.down * 0.25f * (1f - e);
                letter.localRotation = Quaternion.Euler(35f * (1f - e), 0f, 0f) * rot;
                letter.localScale = scale * Mathf.Max(0.001f, Mathf.Lerp(0.6f, 1f, e) * Mathf.Min(1f, t * 3f));
            })));
        }
        return go;
    }

    private void TextOut(GameObject go) { if (go != null) StartCoroutine(TextOutRun(go)); }

    private IEnumerator TextOutRun(GameObject go)
    {
        if (!letters.TryGetValue(go, out var list)) { Kill(go); yield break; }
        letters.Remove(go);
        for (int i = 0; i < list.Count; i++)
        {
            var (letter, pos, rot, scale) = list[i];
            StartCoroutine(Delayed(i * 0.012f, Tween(0.4f, t =>
            {
                if (letter == null) return;
                float e = t * t;
                letter.localPosition = pos + Vector3.up * 0.25f * e;
                letter.localRotation = Quaternion.Euler(-25f * e, 0f, 0f) * rot;
                letter.localScale = scale * Mathf.Max(0.001f, 1f - e);
            })));
        }
        yield return new WaitForSeconds(list.Count * 0.012f + 0.45f);
        Kill(go);
    }

    private static IEnumerator Delayed(float seconds, IEnumerator then)
    {
        yield return new WaitForSeconds(seconds);
        yield return then;
    }

    private static IEnumerator Tween(float seconds, Action<float> step)
    {
        for (float time = 0f; time < seconds; time += Time.deltaTime)
        {
            step(Mathf.Clamp01(time / seconds));
            yield return null;
        }
        step(1f);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    // ---------------------------------------------------------------- the heart's ECG line

    private class Ecg
    {
        private static readonly float[] Wave = { 0.06f, 0.1f, 0.06f, 0f, -0.12f, 1f, -0.38f, 0f, 0f, 0.1f, 0.18f, 0.22f, 0.16f, 0.06f };
        private const int Samples = 110, PerSecond = 60;
        private readonly LineRenderer line;
        private readonly float[] values = new float[Samples];
        private readonly Queue<float> next = new Queue<float>();
        private readonly Vector3 left;
        private readonly float width;
        private float due;

        public Ecg(PlayLab lab, Vector3 middle, float width)
        {
            this.width = width;
            left = middle - Vector3.right * width * 0.5f;
            var go = new GameObject("ECG");
            lab.Track(go);
            line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = lab.red;
            line.positionCount = Samples;
            line.widthMultiplier = 0.018f;
            line.generateLightingData = true;   // lit like the rest
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tick(0f);
        }

        public void Beat(float strength)
        {
            next.Clear();
            foreach (var v in Wave) next.Enqueue(v * strength);
        }

        public void Tick(float dt)
        {
            for (due += dt * PerSecond; due >= 1f; due -= 1f)
            {
                Array.Copy(values, 1, values, 0, Samples - 1);
                values[Samples - 1] = next.Count > 0 ? next.Dequeue() : UnityEngine.Random.Range(-0.015f, 0.015f);
            }
            for (int i = 0; i < Samples; i++)
                line.SetPosition(i, left + new Vector3(width * i / (Samples - 1f), values[i] * 0.2f, 0f));
        }
    }
}
