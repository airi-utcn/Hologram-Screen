using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The AIRI exhibit for the HLD: an official opening, then the institute's research to explore by touch. Built at runtime
/// from the showcase's models, the story's department models and the 3D headlines (SourceModels/Story/
/// make_story_assets.py: Lab_*, Hint_*, Label_*). Numpad 6 (SceneSwitcher), or four fingers held still for 2 s, starts it;
/// Esc or the four fingers again leave it. README.md
/// ("The AIRI exhibit") walks through it.
///
/// Opening (also the resting state): the AIRI building, a red ribbon before the door. A swipe across cuts it: the halves
/// swing down, confetti falls, the red carpet rolls out toward the guests, "Welcome to AIRI".
/// Carousel: swipe to browse the research areas, tap to open one. In every exhibit BACK (bottom) returns.
///  AI for health      the heart: drag to turn, spread to open, tap a part to name it (valves, wall, vessels)
///  Robotics & IoT     steer the arm (drag), grip the crate, set it on the pad
///  Autonomous systems draw a flight path; the drone flies it
///  Cybersecurity      tap the red packets before they reach the lock; leave the legitimate traffic alone
///  5G-SHARE           drag from the mast to each campus; data runs along the links
///  Digital twins      the turbine: circle the fan to run it up, drag to turn, tap a part to name it
///  Our guests         spin the globe, tap your home: a pin, kept from guest to guest
/// </summary>
public class HoloLab : MonoBehaviour
{
    // Same frame as the spots (StoryDirector): heights above the stage, which is Lift above the alcove floor.
    private const float Lift = 1.0f, Mid = 1.25f, TextY = 2.95f, HintY = 2.3f, TextWidth = 2.1f, TextForward = 0.6f;
    private const float IdleSeconds = 40f, ExhibitIdleSeconds = 30f;

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
    private float lastTouch, holdFour;

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
        if (showcase != null && showcase.GetComponent<HoloLab>() == null)
            showcase.gameObject.AddComponent<HoloLab>();
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
        bool toggle = false;   // numpad 6 starts it through SceneSwitcher (Begin), from any experience

        // Four fingers held still for 2 s: the way in and out on the display, with no keyboard.
        bool still = Held() >= 4;
        foreach (var f in fingers) if ((f.pos - f.start).magnitude > Screen.height * 0.04f) still = false;
        if (still) holdFour += Time.deltaTime; else if (Held() < 4) holdFour = 0f;
        if (holdFour > 2f) { holdFour = -1000f; toggle = true; }   // once per hold

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
        StageGrade.Show(true);
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
        FiveGLogo.Show(false);
        Clear();
        if (keyLight != null)
        {
            if (alcoveSpot != null) alcoveSpot.intensity = alcoveSpotIntensity;
            Destroy(keyLight);
        }
        keyLight = null;
        StageGrade.Show(false);
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

    // ---------------------------------------------------------------- the exhibit

    // The opening is also the resting state; then the research carousel and, from it, the exhibits. 40 s without a touch
    // in the carousel starts over from the opening; an exhibit goes back to the carousel after 30 s without one.
    private IEnumerator Loop()
    {
        timedOut = false;
        yield return Opening();
        int at = 0;
        while (!timedOut)
        {
            int pick = -1;
            yield return Explore(at, i => pick = i);
            if (timedOut || pick < 0) break;
            at = pick;
            lastTouch = Time.time;
            yield return Exhibits[pick].open();
            Clear();
        }
        StopAllCoroutines();   // carpet, confetti; this one ends at the yield below
        Clear();
        StartCoroutine(Loop());
    }

    // The carousel's exhibits: model, its size there, its name under it, and the exhibit it opens.
    private (string model, float height, string label, Func<IEnumerator> open)[] Exhibits => new (string, float, string, Func<IEnumerator>)[]
    {
        ("Models/HumanHeart/HumanHeart", 0.8f, "Label_Health", HealthExhibit),
        ("Story/RobotArm", 0.95f, "Label_Robotics", ArmExhibit),
        ("Story/Drone", 0.4f, "Label_Autonomous", DroneExhibit),
        ("Story/Padlock", 0.8f, "Label_Cyber", CyberExhibit),
        ("Story/Tower5G", 1.0f, "Label_5G", FiveGExhibit),
        ("Models/Engine/source/engine_sk", 0.6f, "Label_Turbine", TurbineExhibit),
        ("Models/Globe/Atlas", 0.8f, "Label_Guests", GuestsExhibit),
    };

    private readonly HashSet<GameObject> keep = new HashSet<GameObject>();

    // N: on to the next step (for trying the exhibit out).
    private bool skip;
    private bool TakeSkip() { bool s = skip; skip = false; return s; }

    private bool Idle(float seconds = IdleSeconds)
    {
        if (Time.time - lastTouch < seconds) return false;
        timedOut = true;
        return true;
    }

    // ------------------------------------------------ the official opening: cut the ribbon, the red carpet rolls out

    private IEnumerator Opening()
    {
        confettiMade = 0;
        var plaza = Load("Story/Plaza");
        plaza.transform.localScale *= 1.15f;
        plaza.transform.position = center + Vector3.up * 0.078f;
        var lab = Model("Story/AIRI_Lab", 2.0f, 2.2f, 0.078f);
        lab.transform.position += Vector3.forward * 0.3f;
        foreach (var (x, z, s) in new[] { (-0.9f, -0.45f, 0.85f), (0.88f, -0.55f, 0.8f), (-0.6f, -0.85f, 0.7f), (0.62f, -0.9f, 0.75f),
                                          (-1.02f, 0.15f, 0.8f), (1.02f, 0.05f, 0.85f) })
        {
            var tree = Model("Story/Tree", 0.6f * s, 1f, 0.078f);
            tree.transform.position += new Vector3(x, 0f, z);
        }
        foreach (float x in new[] { -0.52f, 0.52f })   // lamps either side of the carpet and its stanchions
        {
            var lamp = Model("Story/Lamp", 0.38f, 1f, 0.078f);
            lamp.transform.position += new Vector3(x, 0f, -0.75f);
            if (x > 0f) lamp.transform.RotateAround(Middle(lamp), Vector3.up, 180f);
        }

        // The ribbon: between two posts in front of the door, fluttering a little.
        float groundY = center.y + 0.078f, tieY = groundY + 0.5f, z0 = center.z - 0.55f, half = 0.78f;
        foreach (float x in new[] { -half - 0.02f, half + 0.02f })
        {
            var post = Prim(PrimitiveType.Cylinder, "Post", dark);
            post.transform.localScale = new Vector3(0.035f, (tieY - groundY + 0.04f) * 0.5f, 0.035f);
            post.transform.position = new Vector3(center.x + x, (groundY + tieY + 0.04f) * 0.5f, z0);
            var top = Prim(PrimitiveType.Sphere, "Post top", red);
            top.transform.localScale = Vector3.one * 0.06f;
            top.transform.position = new Vector3(center.x + x, tieY + 0.05f, z0);
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
        Vector3 Point(float s, float wave) =>
            new Vector3(center.x - half + 2f * half * s, tieY - 0.07f * (1f - (2f * s - 1f) * (2f * s - 1f)) + wave, z0);
        void Lay(float time)
        {
            for (int i = 0; i < pieces; i++)
            {
                if (ribbon[i] == null) continue;
                Vector3 a = Point(i / (float)pieces, Mathf.Sin(time * 3f + i * 0.5f) * 0.006f);
                Vector3 b = Point((i + 1f) / pieces, Mathf.Sin(time * 3f + (i + 1) * 0.5f) * 0.006f);
                ribbon[i].position = (a + b) * 0.5f;
                ribbon[i].rotation = Quaternion.FromToRotation(Vector3.right, b - a);
            }
            for (int i = 0; i < bow.Count; i++)
                if (bow[i] != null) bow[i].position = Point(0.5f, 0f) + new Vector3((i == 0 ? -1 : i == 1 ? 1 : 0) * 0.07f, 0f, -0.02f);
        }

        var title = Text("Lab_Opening", TextY);
        var hint = Text("Hint_Cut", HintY, true);
        int cut = -1;
        while (cut < 0)   // waits for its first guest, however long
        {
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

        // Cut: the halves swing down from their posts, the bow drops, confetti falls and the red carpet rolls out
        // from the door toward the guests.
        Kill(ribbon[cut].gameObject);
        TextOut(title);
        TextOut(hint);
        StartCoroutine(Confetti(Vector3.zero, 160, true));
        StartCoroutine(Fall(bow, groundY));
        StartCoroutine(Hang(ribbon, 0, cut, new Vector3(center.x - half, tieY, z0), 1f, length, groundY + 0.012f));
        StartCoroutine(Hang(ribbon, cut + 1, pieces, new Vector3(center.x + half, tieY, z0), -1f, length, groundY + 0.012f));
        var door = FindPart(lab.transform, "Door");
        var doorLook = door != null ? door.GetComponent<Renderer>() : null;
        float doorX = doorLook != null ? doorLook.bounds.center.x : center.x;
        float doorZ = doorLook != null ? doorLook.bounds.min.z : center.z - 0.1f;
        yield return new WaitForSeconds(0.5f);
        var path = FindPart(plaza.transform, "Path");   // the concrete path stands above the lawn: lay the carpet on it
        var pathLook = path != null ? path.GetComponent<Renderer>() : null;
        float carpetY = pathLook != null ? pathLook.bounds.max.y + 0.002f : groundY + 0.016f;
        float carpetWidth = pathLook != null ? pathLook.bounds.size.x - 0.03f : 0.3f;
        // As wide as the path, and short of the curved wall, which starts at the path's edge near the front
        // (make_story_assets.py plaza(): radius 0.7 round (0, -0.15), from -80 degrees; about 0.92 out at this scale).
        StartCoroutine(Carpet(doorX, carpetY, doorZ, center.z - 0.86f, carpetWidth));
        yield return new WaitForSeconds(2.6f);   // the carpet out and its stanchions up

        var welcome = Text("Phrase_Welcome", TextY);
        hint = Text("Hint_Begin", HintY, true);
        lastTouch = Time.time;
        float since = Time.time;
        bool go = false;
        while (!go && Time.time - since < 12f)
        {
            foreach (var f in fingers)
                if (f.ended && Time.time - f.since < 0.5f && (f.pos - f.start).magnitude < Screen.height * 0.05f) go = true;
            if (TakeSkip()) go = true;
            yield return null;
        }
        TextOut(welcome);
        TextOut(hint);
        yield return ShrinkAll(0.6f);
    }

    // The red carpet unrolls from (x, zFrom) toward zTo: a roll that shrinks as it goes, the cloth and its graphite
    // edges growing behind it. The display sees the stage almost from the side, where a flat carpet is a thin line, so
    // it is a thick runner, and stanchions with red ropes rise along both sides once it is out.
    private IEnumerator Carpet(float x, float y, float zFrom, float zTo, float width)
    {
        float full = zFrom - zTo;
        const float thick = 0.024f;
        var cloth = Prim(PrimitiveType.Cube, "Carpet", red);
        var edges = new[] { Prim(PrimitiveType.Cube, "Carpet edge", dark), Prim(PrimitiveType.Cube, "Carpet edge", dark) };
        var roll = Prim(PrimitiveType.Cylinder, "Carpet roll", red);
        const float seconds = 1.8f;
        for (float t = 0f; t <= seconds; t += Time.deltaTime)
        {
            float k = Smooth(Mathf.Clamp01(t / seconds)), len = Mathf.Max(0.001f, full * k);
            cloth.transform.localScale = new Vector3(width, thick, len);
            cloth.transform.position = new Vector3(x, y + thick * 0.5f, zFrom - len * 0.5f);
            for (int i = 0; i < 2; i++)
            {
                edges[i].transform.localScale = new Vector3(0.02f, thick + 0.008f, len);
                edges[i].transform.position = new Vector3(x + (i == 0 ? -1f : 1f) * (width * 0.5f + 0.01f), y + (thick + 0.008f) * 0.5f, zFrom - len * 0.5f);
            }
            float r = Mathf.Lerp(0.07f, 0.014f, k);   // what is left on the roll
            roll.transform.localScale = new Vector3(2f * r, width * 0.5f, 2f * r);
            roll.transform.position = new Vector3(x, y + r, zFrom - len);
            roll.transform.rotation = Quaternion.AngleAxis(-len / Mathf.Max(r, 0.01f) * Mathf.Rad2Deg, Vector3.right) * Quaternion.Euler(0f, 0f, 90f);
            yield return null;
        }
        Kill(roll);

        // Stanchions rise one pair after another from the door outward, then the ropes hang between them.
        const int perSide = 3;
        const float postHeight = 0.2f;
        var tops = new Vector3[2, perSide];
        for (int j = 0; j < perSide; j++)
        {
            float z = Mathf.Lerp(zFrom - 0.08f, zTo + 0.06f, j / (perSide - 1f));
            for (int i = 0; i < 2; i++)
            {
                float px = x + (i == 0 ? -1f : 1f) * (width * 0.5f + 0.06f);
                var post = Prim(PrimitiveType.Cylinder, "Stanchion", dark);
                var cap = Prim(PrimitiveType.Sphere, "Stanchion top", light);
                var foot = Prim(PrimitiveType.Cylinder, "Stanchion foot", dark);
                tops[i, j] = new Vector3(px, y + postHeight, z);
                Transform pt = post.transform, ct = cap.transform, ft = foot.transform;
                StartCoroutine(Tween(0.35f, k =>
                {
                    float h = postHeight * EaseOutBack(k);
                    if (pt != null) { pt.localScale = new Vector3(0.022f, Mathf.Max(0.001f, h * 0.5f), 0.022f); pt.position = new Vector3(px, y + h * 0.5f, z); }
                    if (ct != null) { ct.localScale = Vector3.one * 0.04f * Mathf.Max(0.02f, k); ct.position = new Vector3(px, y + h + 0.01f, z); }
                    if (ft != null) { ft.localScale = new Vector3(0.06f, 0.006f, 0.06f); ft.position = new Vector3(px, y + 0.006f, z); }
                }));
            }
            yield return new WaitForSeconds(0.15f);
        }
        yield return new WaitForSeconds(0.25f);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j + 1 < perSide; j++)
            {
                var rope = Line(red, 0.014f);
                rope.positionCount = 12;
                Vector3 a = tops[i, j] + Vector3.down * 0.015f, b = tops[i, j + 1] + Vector3.down * 0.015f;
                StartCoroutine(Tween(0.4f, k =>
                {
                    if (rope == null) return;
                    for (int q = 0; q < 12; q++)
                    {
                        float u = q / 11f;
                        rope.SetPosition(q, Vector3.Lerp(a, b, u) + Vector3.down * 0.05f * EaseOutBack(k) * 4f * u * (1f - u));
                    }
                }));
            }
    }

    // ------------------------------------------------ the research carousel

    // A cover flow: the exhibit in front large and turning on its plinth, its neighbours smaller, set back and turned
    // toward it, the rest out of sight; beads under it say where you are. Swipe to browse (a flick runs on and settles),
    // tap to open the one in front.
    private IEnumerator Explore(int start, Action<int> chosen)
    {
        var list = Exhibits;
        int n = list.Length;
        var title = Text("Lab_Explore", TextY);
        var hint = Text("Hint_Browse", HintY, true);
        var pivots = new List<Transform>();
        var halfHeight = new List<float>();
        var plinths = new List<Transform>();
        foreach (var e in list)
        {
            var model = Model(e.model, e.height, 0.85f, 0f);
            halfHeight.Add(GesturePlayer.GetBounds(Parts(model)).extents.y);
            pivots.Add(Pivot(model));
            var plinth = Prim(PrimitiveType.Cylinder, "Plinth", dark).transform;
            plinth.localScale = new Vector3(0.62f, 0.02f, 0.62f);
            var rim = Prim(PrimitiveType.Cylinder, "Plinth rim", red).transform;
            rim.SetParent(plinth, false);
            rim.localScale = new Vector3(1.04f, 0.5f, 1.04f);
            rim.localPosition = Vector3.down * 0.6f;
            plinths.Add(plinth);
        }
        var beads = Row(new Vector3(-0.3f, 0.3f, 0f), new Vector3(0.3f, 0.3f, 0f), n, 0.05f);

        float offset = start, velocity = 0f;   // in exhibits; the one in front is round(offset)
        int front = -1, pick = -1;
        GameObject label = null;
        lastTouch = Time.time;
        while (pick < 0)
        {
            if (Idle()) { Clear(); yield break; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (TakeSkip()) pick = Mod(Mathf.RoundToInt(offset), n);
            if (Held() == 1 && !fingers[0].began)
            {
                float d = -fingers[0].delta.x / Screen.width * 2.2f;   // a full-width swipe moves about two exhibits
                offset += d;
                velocity = Mathf.Lerp(velocity, d / dt, 0.4f);
            }
            else
            {
                offset += velocity * dt;
                velocity = Mathf.Lerp(velocity, 0f, dt * 4f);
                if (Mathf.Abs(velocity) < 0.6f) offset = Mathf.Lerp(offset, Mathf.Round(offset), dt * 7f);   // settles on one
            }
            foreach (var f in fingers)
                if (f.ended && Time.time - f.since < 0.4f && (f.pos - f.start).magnitude < Screen.height * 0.03f)
                    pick = Mod(Mathf.RoundToInt(offset), n);

            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Repeat(i - offset + n * 0.5f, n) - n * 0.5f;   // signed place from the front, wrapping round
                float side = Mathf.Clamp(d, -1.6f, 1.6f), away = Mathf.Abs(side);
                float scale = Mathf.Lerp(1.15f, 0.55f, Mathf.Clamp01(away)) * Mathf.Clamp01(1.6f - Mathf.Abs(d));   // gone past 1.6
                float floorY = center.y + 0.78f + 0.12f * Mathf.Clamp01(1f - away);
                Vector3 at = new Vector3(center.x + side * 0.82f, floorY, center.z + 0.45f * Mathf.Clamp01(away));
                var p = pivots[i];
                p.localScale = Vector3.one * Mathf.Max(0.001f, scale);
                p.position = at + Vector3.up * (halfHeight[i] * scale + 0.02f);
                // the front one turns slowly on its plinth; the others face it
                Quaternion face = Quaternion.Euler(0f, -side * 35f, 0f);
                p.rotation = away < 0.5f ? Quaternion.Euler(0f, Time.time * 25f, 0f) : Quaternion.Slerp(p.rotation, face, dt * 6f);
                plinths[i].position = at;
                plinths[i].localScale = new Vector3(0.62f, 0.02f, 0.62f) * Mathf.Max(0.001f, scale);
            }
            int now = Mod(Mathf.RoundToInt(offset), n);
            if (now != front)
            {
                front = now;
                TextOut(label);
                label = Text(list[front].label, 0.5f, true);
                FiveGLogo.Show(list[front].label == "Label_5G");
                for (int i = 0; i < n; i++) if (beads[i] != null) beads[i].sharedMaterial = i == front ? red : light;
            }
            yield return null;
        }
        TextOut(title);
        TextOut(hint);
        TextOut(label);
        FiveGLogo.Show(false);
        for (int i = 0; i < n; i++) if (i != pick) StartCoroutine(Shrink(pivots[i].gameObject, 0.35f));
        var chosenPivot = pivots[pick];
        Vector3 big = chosenPivot.localScale;
        yield return Tween(0.4f, t => { if (chosenPivot != null) chosenPivot.localScale = big * (1f + 0.2f * Mathf.Sin(t * Mathf.PI)); });
        yield return ShrinkAll(0.3f);
        chosen(pick);
    }

    private static int Mod(int a, int n) => ((a % n) + n) % n;

    // ------------------------------------------------ what every exhibit shares

    private GameObject backLabel;

    private void ExhibitStart()
    {
        backLabel = Text("Hint_Back", 0.12f, true);
        lastTouch = Time.time;
    }

    // A tap low on the display, where BACK is.
    private bool BackTapped()
    {
        float limit = cam.WorldToScreenPoint(center + Vector3.up * 0.24f).y;
        foreach (var f in fingers)
            if (f.ended && Time.time - f.since < 0.4f && (f.pos - f.start).magnitude < Screen.height * 0.03f && f.pos.y < limit) return true;
        return false;
    }

    // Back to the carousel: BACK, 30 s without a touch, or N.
    private bool Leaving() => Time.time - lastTouch > ExhibitIdleSeconds || BackTapped() || TakeSkip();

    // The demonstration is done: the hint goes.
    private GameObject Completed(GameObject hint)
    {
        TextOut(hint);
        return null;
    }

    private IEnumerator Close(params GameObject[] texts)
    {
        FiveGLogo.Show(false);
        foreach (var t in texts) TextOut(t);
        TextOut(backLabel);
        yield return ShrinkAll(0.45f);
    }

    // Drag to turn (it keeps turning, slowing to a slow spin), spread two fingers to open, tap a part to name it.
    private class Inspector
    {
        public Transform pivot;
        public Vector2 spin = new Vector2(12f, 0f);
        public bool hold;
        public float explode, pinch0 = -1f, side;
        public GameObject label;
        public Renderer target;
        public LineRenderer leader;
    }

    private void Inspect(Inspector s, Action<float> open, Renderer[] parts, Func<Renderer, string> nameOf, bool turn = true)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        if (s.hold)   // held still (the turbine while its fan is run up); the slow turn comes back gently afterwards
            s.spin = Vector2.zero;
        else if (turn && Held() == 1 && !fingers[0].began)
            s.spin = Vector2.Lerp(s.spin, new Vector2(-fingers[0].delta.x, fingers[0].delta.y) / dt / Screen.height * 180f, 0.5f);
        else
            s.spin = Vector2.Lerp(s.spin, new Vector2(12f, 0f), dt * 0.8f);
        s.pivot.Rotate(Vector3.up, s.spin.x * dt, Space.World);
        s.pivot.Rotate(Vector3.right, s.spin.y * dt, Space.World);
        if (open != null)
        {
            s.explode = Mathf.MoveTowards(s.explode, Spread(ref s.pinch0), dt * 2.5f);
            open(s.explode);
        }

        foreach (var f in fingers)
        {
            if (!(f.ended && Time.time - f.since < 0.4f && (f.pos - f.start).magnitude < Screen.height * 0.03f)) continue;
            Renderer best = null;
            float bestD = Screen.height * 0.1f;
            foreach (var r in parts)
            {
                if (r == null) continue;
                float d = Vector2.Distance(f.pos, cam.WorldToScreenPoint(r.bounds.center));
                if (d < bestD) { best = r; bestD = d; }
            }
            if (best == null || best == s.target) continue;
            TextOut(s.label);
            s.target = best;
            s.side = cam.WorldToScreenPoint(best.bounds.center).x > Screen.width * 0.5f ? 1f : -1f;
            s.label = Text(nameOf(best), Mathf.Clamp(best.bounds.center.y - center.y, 0.55f, 1.95f), true, s.side * 0.66f, 0.85f);
            if (s.leader == null) { s.leader = Line(dark, 0.006f); s.leader.positionCount = 2; }
        }
        if (s.target != null && s.label != null && s.leader != null)
        {
            var labelParts = Parts(s.label);
            if (labelParts.Length == 0) return;
            Bounds lb = GesturePlayer.GetBounds(labelParts);
            s.leader.SetPosition(0, s.target.bounds.center);
            s.leader.SetPosition(1, new Vector3(s.side > 0f ? lb.min.x - 0.03f : lb.max.x + 0.03f, lb.center.y, lb.center.z));
        }
    }

    // ------------------------------------------------ AI for health: the heart, opened and named part by part

    private IEnumerator HealthExhibit()
    {
        var heart = Model("Models/HumanHeart/HumanHeart", 1.45f, 1.6f, 0.5f);
        var pivot = Pivot(heart);
        var open = Opener(heart, pivot.position, 0.35f, 0.9f);
        var parts = Parts(heart);
        var title = Text("Phrase_Health", TextY);
        var hint = Text("Hint_Inspect", HintY, true);
        ExhibitStart();
        string Name(Renderer r)
        {
            string n = r.name;
            return n.Contains("Mitral") ? "Label_Mitral" : n.Contains("Tricuspid") ? "Label_Tricuspid" : n.Contains("Aortic") ? "Label_Aortic"
                 : n.Contains("Pulmonary") ? "Label_Pulmonary" : n.Contains("Vains") || n.Contains("Vein") ? "Label_Vessels" : "Label_Wall";
        }
        var look = new Inspector { pivot = pivot };
        var outside = Array.FindAll(parts, r => Name(r) == "Label_Wall" || Name(r) == "Label_Vessels");
        while (!Leaving())
        {
            Inspect(look, open, look.explode > 0.3f ? parts : outside, Name);   // the valves can be named once it is open
            float beat = (Time.time % 1f);   // a calm 60 bpm
            pivot.localScale = Vector3.one * (1f - 0.035f * Mathf.Exp(-beat * 9f) - 0.02f * Mathf.Exp(-Mathf.Abs(beat - 0.25f) * 14f));
            yield return null;
        }
        yield return Close(title, hint, look.label);
    }

    // ------------------------------------------------ digital twins: the turbine, run up by circling its fan

    private IEnumerator TurbineExhibit()
    {
        var engine = Model("Models/Engine/source/engine_sk", 1.25f, 1.9f, 0.75f);
        Transform rotor = FindPart(engine.transform, "ROTOR");
        var rotorLook = rotor != null ? rotor.GetComponent<Renderer>() : null;
        if (rotorLook != null)   // the fan to the right: the slow turn brings it round to face the viewer straight away
        {
            Vector3 toFan = rotorLook.bounds.center - Middle(engine);
            toFan.y = 0f;
            engine.transform.RotateAround(Middle(engine), Vector3.up, Vector3.SignedAngle(toFan, Vector3.right, Vector3.up));
            Fit(engine, 1.25f, 1.9f, 0.75f);
        }
        var pivot = Pivot(engine);
        Vector3 mid = pivot.position;
        Vector3 fan = rotorLook != null ? rotorLook.bounds.center : mid;
        Vector3 fanLocal = pivot.InverseTransformPoint(fan);
        var parts = Parts(engine);
        // The exhaust leaves the back of the engine: the fan was just turned to the right, so the back is on the left.
        Bounds eb = GesturePlayer.GetBounds(parts);
        Vector3 rearLocal = pivot.InverseTransformPoint(new Vector3(eb.min.x + 0.05f, fan.y, fan.z));
        Vector3 backLocal = pivot.InverseTransformDirection(Vector3.left);
        var streaks = new List<(Transform t, Vector3 v, float life)>();

        // The fan's hub and axis, kept in the rotor's own space so they stay right however the engine is turned. The engine
        // was just turned with its fan to the right, so its long axis, the fan's axis, is the world's right at this moment.
        // (The line from the engine's middle to the fan tilts, the pylon on top lifting the middle, and the fan wobbled;
        // the file's own "Fan" clip did not play through a PlayableGraph here.)
        bool spins = rotor != null && rotorLook != null;
        Vector3 hub = spins ? rotor.InverseTransformPoint(rotorLook.bounds.center) : Vector3.zero;
        Vector3 spinAxis = spins ? rotor.InverseTransformDirection(Vector3.right).normalized : Vector3.right;
        // Explicitly, from the rest pose each frame (to the hub, turn, back): nothing accumulates, so it cannot drift.
        Vector3 restPosition = spins ? rotor.localPosition : Vector3.zero;
        Quaternion restRotation = spins ? rotor.localRotation : Quaternion.identity;
        Vector3 hubScaled = spins ? Vector3.Scale(rotor.localScale, hub) : Vector3.zero;   // the hub in the parent's units
        float fanAngle = 0f;
        string Name(Renderer r) => r.name.Contains("ROTOR") ? "Label_Fan" : r.name.Contains("PUSH") ? "Label_Reverser" : "Label_Casing";
        var title = Text("Title_Turbine", TextY);
        var hint = Text("Hint_Turbine", HintY, true);
        var gauge = Row(new Vector3(-0.7f, 0.42f, 0f), new Vector3(0.7f, 0.42f, 0f), 24, 0.04f);   // the run-up, red at the top
        ExhibitStart();
        const float zone = 0.75f;
        var look = new Inspector { pivot = pivot };
        var circling = new HashSet<int>();
        float power = 0f, inZone = 0f, doneAt = -1f, lastCircle = -10f;
        while (!Leaving() && (doneAt < 0f || Time.time - doneAt < 6f))
        {
            float dt = Time.deltaTime;
            Vector3 fanNow = pivot.TransformPoint(fanLocal);
            Vector2 c = cam.WorldToScreenPoint(fanNow);
            float reach = Screen.height * 0.16f;
            foreach (var f in fingers)
            {
                if (f.began && Vector2.Distance(f.start, c) < reach) circling.Add(f.id);   // started on the fan: it runs it up
                if (f.ended) { circling.Remove(f.id); continue; }
                if (f.began || !circling.Contains(f.id)) continue;
                Vector2 a = f.pos - f.delta - c, b = f.pos - c;
                if (a.magnitude > 8f && b.magnitude > 8f) power += Mathf.Abs(Vector2.SignedAngle(a, b)) / 360f * 0.16f;
            }
            if (circling.Count > 0) lastCircle = Time.time;
            look.hold = Time.time - lastCircle < 0.7f;   // the engine stays put while the fan is spun, and 0.7 s after
            Inspect(look, null, parts, Name, circling.Count == 0);
            power = Mathf.Clamp(power - dt * 0.1f * (0.5f + power), 0f, 1.15f);
            if (power >= zone) inZone += dt;
            Fill(gauge, power, zone);
            if (spins)   // a slow idle turn, up to 7 turns a second at full power
            {
                fanAngle = Mathf.Repeat(fanAngle + Mathf.Lerp(0.3f, 7f, Mathf.Clamp01(power)) * 360f * dt, 360f);
                Quaternion turned = restRotation * Quaternion.AngleAxis(fanAngle, spinAxis);
                rotor.localRotation = turned;
                rotor.localPosition = restPosition + restRotation * hubScaled - turned * hubScaled;   // the hub stays put
            }
            if (power > 0.25f && UnityEngine.Random.value < power * 0.9f)   // the air blown out of the back
            {
                var s = Prim(PrimitiveType.Cube, "Exhaust", light, false);
                Vector3 back = pivot.TransformDirection(backLocal);
                s.transform.position = pivot.TransformPoint(rearLocal) + UnityEngine.Random.insideUnitSphere * 0.12f;
                s.transform.rotation = Quaternion.LookRotation(back);
                s.transform.localScale = new Vector3(0.012f, 0.012f, 0.12f + 0.25f * power);
                streaks.Add((s.transform, back * (1.5f + 3f * power), 0.45f));
            }
            for (int i = streaks.Count - 1; i >= 0; i--)
            {
                var (t, v, life) = streaks[i];
                life -= dt;
                if (t == null || life <= 0f) { if (t != null) Kill(t.gameObject); streaks.RemoveAt(i); continue; }
                t.position += v * dt;
                streaks[i] = (t, v, life);
            }
            if (doneAt < 0f && inZone >= 3f) { doneAt = Time.time; hint = Completed(hint); }
            yield return null;
        }
        yield return Close(title, hint, look.label);
    }

    // ------------------------------------------------ robotics & IoT: steer the arm, put the crate on the pad

    private IEnumerator ArmExhibit()
    {
        var arm = Model("Story/RobotArm", 1.35f, 1.4f, 0.3f);
        Transform Part(string n) => FindPart(arm.transform, n);
        Transform turret = Part("Turret"), upper = Part("UpperArm"), fore = Part("Forearm"), wrist = Part("Wrist"),
                  fingerL = Part("FingerL"), fingerR = Part("FingerR"), crate = Part("Crate"), basePart = Part("Base");
        var title = Text("Dept_Robotics", TextY);
        var hint = Text("Hint_Arm", HintY, true);
        ExhibitStart();
        if (turret == null || upper == null || fore == null || wrist == null || crate == null)
        {
            while (!Leaving()) yield return null;
            yield return Close(title, hint);
            yield break;
        }
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
        float Toward(Transform joint, Vector3 axis, Quaternion rest)   // which way bends the hand down to the crate
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
        void Pose(float yaw, float reach, float close)   // reach 0 = up, 1 = down at the crate (the spot's 28 / 22 degrees)
        {
            turret.localRotation = Quaternion.AngleAxis(yaw, yawAxis) * restT;
            upper.localRotation = Quaternion.AngleAxis(su * 28f * reach, pitchU) * restU;
            fore.localRotation = Quaternion.AngleAxis(sf * 22f * reach, pitchF) * restF;
            if (fingerL != null) fingerL.localPosition = lRest + grip * close;
            if (fingerR != null) fingerR.localPosition = rRest - grip * close;
        }
        Pose(110f, 0.75f, 0f);   // the pad: where the hand comes down with the arm swung round
        Vector3 padAt = new Vector3(wrist.position.x, center.y + 0.3f, wrist.position.z);
        Pose(0f, 1f, 0f);
        float reachGrip = Vector3.Distance(wrist.position, crate.position) + 0.12f;
        Pose(0f, 0f, 0f);
        var pad = Prim(PrimitiveType.Cylinder, "Pad", red);
        pad.transform.localScale = new Vector3(0.42f, 0.006f, 0.42f);
        pad.transform.position = padAt;
        float floorY = center.y + 0.3f;
        Bounds CrateBounds() => GesturePlayer.GetBounds(crate.GetComponentsInChildren<Renderer>());

        float yawNow = 0f, reachNow = 0f, close = 0f, doneAt = -1f;
        bool holding = false, lifted = false;
        while (!Leaving() && (doneAt < 0f || Time.time - doneAt < 6f))
        {
            if (Held() == 1 && doneAt < 0f)
            {
                var f = fingers[0];
                yawNow = Mathf.Clamp(yawNow - f.delta.x / Screen.height * 220f, -170f, 170f);
                reachNow = Mathf.Clamp01(reachNow - f.delta.y / Screen.height * 2.5f);   // drag down: the arm reaches down
            }
            if (doneAt >= 0f) reachNow = Mathf.MoveTowards(reachNow, 0f, Time.deltaTime);
            close = Mathf.MoveTowards(close, holding ? 1f : 0f, Time.deltaTime * 4f);
            Pose(yawNow, reachNow, close);
            if (!holding && doneAt < 0f && reachNow > 0.6f)   // the hand down over the crate: it grips
            {
                Bounds cb = CrateBounds();
                Vector2 over = new Vector2(wrist.position.x - cb.center.x, wrist.position.z - cb.center.z);
                if (over.magnitude < 0.22f || Vector3.Distance(wrist.position, crate.position) < reachGrip)
                {
                    holding = true;
                    lifted = false;
                    crate.SetParent(wrist, true);
                }
            }
            if (holding && reachNow < 0.4f) lifted = true;
            bool overPad = holding && new Vector2(crate.position.x - padAt.x, crate.position.z - padAt.z).magnitude < 0.32f;
            pad.transform.localScale = new Vector3(0.42f, 0.006f, 0.42f) * (overPad ? 1f + 0.12f * Mathf.Sin(Time.time * 10f) : 1f);
            if (holding && lifted && reachNow > 0.55f)   // lifted, then lowered again: it is set down below the hand
            {
                holding = false;
                crate.SetParent(arm.transform, true);
                Bounds cb = CrateBounds();
                Vector3 at = overPad ? padAt : new Vector3(cb.center.x, floorY, cb.center.z);   // near the pad it lands on it
                crate.position += new Vector3(at.x - cb.center.x, at.y + 0.005f - cb.min.y, at.z - cb.center.z);
                if (overPad)
                {
                    doneAt = Time.time;
                    hint = Completed(hint);
                }
            }
            yield return null;
        }
        yield return Close(title, hint);
    }

    // ------------------------------------------------ autonomous systems: draw a flight path, the drone flies it

    private IEnumerator DroneExhibit()
    {
        var drone = Model("Story/Drone", 0.32f, 0.75f, 1.2f);
        var body = Pivot(drone);
        var rotors = new List<Transform>();
        foreach (var t in drone.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Rotor")) rotors.Add(t);
        var title = Text("Dept_Autonomous", TextY);
        var hint = Text("Hint_Path", HintY, true);
        ExhibitStart();

        var path = new List<Vector3>();
        var line = Line(red, 0.012f);
        line.positionCount = 0;
        int drawing = int.MinValue;
        bool flying = false;
        float along = 0f, doneAt = -1f;
        Vector3 pos = body.position, vel = Vector3.zero;
        Vector3 Clamp(Vector3 p) => new Vector3(Mathf.Clamp(p.x, center.x - 1.1f, center.x + 1.1f), Mathf.Clamp(p.y, center.y + 0.35f, center.y + 2.4f), center.z);
        while (!Leaving() && (doneAt < 0f || Time.time - doneAt < 6f))
        {
            float dt = Time.deltaTime;
            foreach (var f in fingers)
            {
                if (f.began && drawing == int.MinValue)   // a new path, from the drone
                {
                    drawing = f.id;
                    flying = false;
                    path.Clear();
                    path.Add(pos);
                    path.Add(Clamp(OnPlane(f.pos, center.z)));
                }
                if (f.id != drawing) continue;
                Vector3 p = Clamp(OnPlane(f.pos, center.z));
                if ((p - path[path.Count - 1]).magnitude > 0.03f) path.Add(p);
                if (f.ended)
                {
                    drawing = int.MinValue;
                    flying = path.Count > 4;
                    along = 0f;
                }
            }
            line.positionCount = path.Count;
            line.SetPositions(path.ToArray());

            Vector3 target = pos;
            if (flying)
            {
                along += 0.8f * dt;   // metres along the path
                float walked = 0f, total = 0f;
                for (int i = 1; i < path.Count; i++) total += (path[i] - path[i - 1]).magnitude;
                target = path[path.Count - 1];
                for (int i = 1; i < path.Count; i++)
                {
                    float seg = (path[i] - path[i - 1]).magnitude;
                    if (walked + seg >= along) { target = Vector3.Lerp(path[i - 1], path[i], (along - walked) / Mathf.Max(seg, 1e-4f)); break; }
                    walked += seg;
                }
                if (along >= total && (target - pos).magnitude < 0.05f)   // the whole path flown
                {
                    flying = false;
                    if (doneAt < 0f) { doneAt = Time.time; hint = Completed(hint); }
                }
            }
            vel = Vector3.Lerp(vel, (target - pos) * 6f, dt * 6f);
            pos += vel * dt + Vector3.up * Mathf.Sin(Time.time * 2.6f) * 0.0012f;
            body.SetPositionAndRotation(pos, Quaternion.Euler(Mathf.Clamp(vel.y * 6f, -15f, 15f), 0f, Mathf.Clamp(-vel.x * 12f, -25f, 25f)));
            foreach (var r in rotors) if (r != null) r.Rotate(body.up, 1400f * dt, Space.World);
            yield return null;
        }
        yield return Close(title, hint);
    }

    // ------------------------------------------------ cybersecurity: intercept the red packets before they reach the lock

    private IEnumerator CyberExhibit()
    {
        var padlock = Model("Story/Padlock", 0.75f, 0.8f, Mid - 0.38f);
        var pivot = Pivot(padlock);
        Vector3 mid = pivot.position;
        var title = Text("Dept_Cyber", TextY);
        var hint = Text("Hint_Intercept", HintY, true);
        var shield = Arc(mid, 0.6f, 30, 0.04f, 90f, 360f);
        Fill(shield, 0f);
        ExhibitStart();

        const int need = 12;
        var packets = new List<(Transform t, bool attack, float speed)>();
        int blocked = 0;
        float nextPacket = 0f, shake = 0f, start = Time.time, doneAt = -1f;
        while (!Leaving() && (doneAt < 0f || Time.time - doneAt < 5f))
        {
            float dt = Time.deltaTime;
            if (doneAt < 0f && Time.time >= nextPacket)   // traffic from all round, a little faster as it goes on
            {
                nextPacket = Time.time + Mathf.Lerp(0.9f, 0.5f, (Time.time - start) / 40f);
                bool attack = UnityEngine.Random.value < 0.6f;
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 from = mid + new Vector3(Mathf.Cos(a) * 1.15f, Mathf.Sin(a) * 1.3f, 0f);
                from.y = Mathf.Clamp(from.y, center.y + 0.35f, center.y + 2.5f);
                var p = Prim(attack ? PrimitiveType.Sphere : PrimitiveType.Cube, attack ? "Attack" : "Data", attack ? red : dark, false);
                p.transform.position = from;
                p.transform.localScale = Vector3.one * (attack ? 0.1f : 0.07f);
                packets.Add((p.transform, attack, UnityEngine.Random.Range(0.3f, 0.45f)));
            }
            foreach (var f in fingers)
            {
                if (!f.began || doneAt >= 0f) continue;
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
                Burst(t.position, attack ? 10 : 5, attack ? red : dark, 0.8f, 0.022f);
                if (attack) blocked++;
                else { blocked = Mathf.Max(0, blocked - 1); shake = 1f; }   // that was legitimate traffic
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
                    if (attack) { shake = 1f; blocked = Mathf.Max(0, blocked - 2); Fill(shield, blocked / (float)need); }
                    Kill(t.gameObject);
                    packets.RemoveAt(i);
                }
            }
            shake = Mathf.MoveTowards(shake, 0f, dt * 3f);
            pivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 50f) * 8f * shake);
            if (doneAt < 0f && blocked >= need)
            {
                doneAt = Time.time;
                hint = Completed(hint);
                foreach (var (t, _, _) in packets) if (t != null) Kill(t.gameObject);
                packets.Clear();
            }
            yield return null;
        }
        yield return Close(title, hint);
    }

    // ------------------------------------------------ 5G-SHARE: link the mast to each campus

    private IEnumerator FiveGExhibit()
    {
        var tower = Model("Story/Tower5G", 1.65f, 0.9f, 0.3f);
        tower.transform.position += Vector3.forward * 0.2f;
        Bounds tb = GesturePlayer.GetBounds(Parts(tower));
        Vector3 top = new Vector3(tb.center.x, tb.max.y - 0.1f, tb.center.z);
        Vector2[] spots = { new Vector2(-0.92f, -0.45f), new Vector2(0.92f, -0.45f), new Vector2(-0.72f, 0.75f), new Vector2(0.72f, 0.75f) };
        var hubs = new List<Vector3>();
        foreach (var s in spots)
        {
            var hub = Model("Story/Hub", 0.38f, 0.5f, 0.3f);
            hub.transform.position += new Vector3(s.x, 0f, s.y);
            Bounds hb = GesturePlayer.GetBounds(Parts(hub));
            hubs.Add(new Vector3(hb.center.x, hb.max.y, hb.center.z));
        }
        var title = Text("Phrase_5G", TextY);
        var hint = Text("Hint_5G", HintY, true);
        ExhibitStart();
        FiveGLogo.Show(true);

        var linked = new bool[hubs.Count];
        int links = 0, dragging = int.MinValue;
        float doneAt = -1f;
        LineRenderer cable = null;
        while (!Leaving() && (doneAt < 0f || Time.time - doneAt < 6f))
        {
            Vector2 mast = cam.WorldToScreenPoint(top), foot = cam.WorldToScreenPoint(new Vector3(top.x, tb.min.y, top.z));
            foreach (var f in fingers)
            {
                if (f.began && dragging == int.MinValue && Mathf.Abs(f.pos.x - mast.x) < Screen.height * 0.08f && f.pos.y > foot.y - 20f && f.pos.y < mast.y + Screen.height * 0.06f)
                {
                    dragging = f.id;
                    cable = Line(red, 0.014f);
                }
                if (f.id != dragging || cable == null) continue;
                SetArc(cable, top, OnPlane(f.pos, top.z), 0.15f);
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
                }
                else Kill(cable.gameObject);
                cable = null;
                dragging = int.MinValue;
            }
            if (dragging != int.MinValue && !fingerById.ContainsKey(dragging)) { if (cable != null) Kill(cable.gameObject); cable = null; dragging = int.MinValue; }
            if (doneAt < 0f && links == hubs.Count)
            {
                doneAt = Time.time;
                hint = Completed(hint);
                StartCoroutine(Waves(new Vector3(top.x, tb.min.y + 0.02f, top.z)));
            }
            yield return null;
        }
        yield return Close(title, hint);
    }

    private IEnumerator Waves(Vector3 at)
    {
        for (int w = 0; w < 3; w++)
        {
            var ring = Load("Story/Ring");
            ring.transform.position = at;
            StartCoroutine(Tween(1.2f, t => { if (ring != null) ring.transform.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Lerp(0.15f, 1.2f, t) * (1f - t * t)); }));
            yield return new WaitForSeconds(0.35f);
        }
    }

    // ------------------------------------------------ our guests: where everyone came from

    private IEnumerator GuestsExhibit()
    {
        var globe = Model("Models/Globe/Atlas", 1.45f, 1.45f, 0.55f);
        var pivot = Pivot(globe);
        Vector3 mid = pivot.position;
        float radius = GesturePlayer.GetBounds(Parts(globe)).extents.y;
        foreach (var dir in LoadPins())   // the guests before: graphite pins
            Pin(globe.transform, mid, radius, globe.transform.TransformDirection(dir), false);
        var title = Text("Lab_Globe", TextY);
        var hint = Text("Hint_Globe", HintY, true);
        ExhibitStart();

        float yaw = 0f, yawSpeed = 15f, tilt = 0f;
        while (!Leaving())
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            foreach (var f in fingers)
            {
                if (!f.began && !f.ended && Held() == 1)   // drag: spin and tilt; a flick keeps it turning
                {
                    yawSpeed = Mathf.Lerp(yawSpeed, -f.delta.x / dt / Screen.height * 220f, 0.5f);
                    tilt = Mathf.Clamp(tilt + f.delta.y / Screen.height * 90f, -30f, 30f);
                }
                bool tap = f.ended && Time.time - f.since < 0.35f && (f.pos - f.start).magnitude < Screen.height * 0.03f;
                if (tap && HitSphere(f.pos, mid, radius, out Vector3 hit))
                {
                    Pin(globe.transform, mid, radius, hit - mid, true);
                    SavePin(globe.transform.InverseTransformDirection((hit - mid).normalized));
                }
            }
            yawSpeed = Mathf.Lerp(yawSpeed, 15f, dt * 0.6f);
            yaw += yawSpeed * dt;
            pivot.rotation = Quaternion.AngleAxis(tilt, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
            yield return null;
        }
        yield return Close(title, hint);
    }

    // ------------------------------------------------ helpers of the exhibits

    private const string PinsKey = "HLD_LabPins";   // every visitor's pin, kept between runs (PlayerPrefs)
    private const int MaxPins = 200;
    private int confettiMade;

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

    private static bool Crosses(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
    {
        float Side(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
        return Side(a, b, p) * Side(a, b, q) < 0f && Side(p, q, a) * Side(p, q, b) < 0f;
    }

    // Ribbon pieces [from, to) swing down from `anchor` (side +1: hanging off the left post, -1: the right one).
    // What reaches the ground lies along it, toward the middle (the halves are longer than the posts are tall).
    private IEnumerator Hang(List<Transform> ribbon, int from, int to, Vector3 anchor, float side, float length, float groundY)
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
                float s = length * (k + 0.5f), down = anchor.y - groundY - 0.006f;
                if (dir.y < -0.01f && -dir.y * s > down)
                {
                    float reach = down / -dir.y;   // where it meets the ground
                    ribbon[i].position = new Vector3(anchor.x + dir.x * reach + side * (s - reach), groundY + 0.006f, anchor.z);
                    ribbon[i].rotation = Quaternion.Euler(90f, 0f, 0f);
                }
                else
                {
                    ribbon[i].position = anchor + dir * s;
                    ribbon[i].rotation = Quaternion.FromToRotation(Vector3.right, dir * side);
                }
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
            Debug.LogWarning($"HoloLab: nothing at Resources/{path}");
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
}
