using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Two spots for the hologram, each on its own key: Enter plays the AIRI x UTCN spot, M the health spot (again: from
/// the start; Esc skips to the end). The app starts on the showcase as usual and returns to it after a spot.
/// One thread runs through each: every image turns into the next through a swarm of particles that leaves one shape
/// and gathers into the next, so the story flows instead of things popping in and out.
///
/// 1. AIRI x UTCN (~83 s). A red spark drifts in and comes to rest: EVERY REVOLUTION STARTS WITH A SPARK. A neural
///    network grows out of it, signals running along its links: IT LEARNS. The neurons settle onto a globe as city
///    lights while the spark circles it: IT CONNECTS THE WORLD. The world becomes a beating heart: BUT IT NEEDS A
///    HUMAN HEART. The spark spirals into the heart and the two become the AIRI logo, standing on the floor: AT UTCN,
///    THEY MEET. The logo pours down into a campus: a lawn spreads, the AIRI building (after the architects' render:
///    seven storeys of light fins, its front curving in around the old house) assembles floor by floor, trees grow:
///    WELCOME TO AIRI. THE AI RESEARCH INSTITUTE AT UTCN. Then its departments and centres
///    (airi.utcluj.ro/research/departments), a quick beat each, flowing into one another: a real-looking AI board
///    (MICROELECTRONICS.), a robot arm lifting a crate (ROBOTICS & IOT.), a car scanning with its lidar under a drone
///    (AUTONOMOUS SYSTEMS.), a padlock circled by a satellite (CYBERSECURITY & SPACE.), the OCT retina scan with its
///    fluids found (ROBOTICS FOR HEALTHCARE.), a balance settling level (TRUSTWORTHY AI.), a gauge and a growing chart
///    (AI MEASUREMENT.), a VR headset among virtual windows (IMMERSIVE EDUCATION.), objects getting boxes and labels
///    (DATA LABELLING.), a server rack blinking (HPC-AI.). Then 5G-SHARE, AIRI's EU project linking four campuses:
///    a 5G mast sends waves and data along arcs to campus hubs in Ireland, Romania, the Czech Republic and Italy
///    (5G-SHARE. 4 COUNTRIES. ONE 5G NETWORK.). It all becomes the AIRI and UTCN logos: THE FUTURE STARTS HERE. JOIN US.
/// 2. AI for health (~22 s), with the OCT retina scan (W_I_OS): a scan beam passes and the fluid pockets show and
///    pulse: AI FOR HEALTH. IT SEES WHAT WE MISS. EARLY DETECTION. SAVED SIGHT. It becomes the AIRI and UTCN logos,
///    side by side on the floor: AIRI x UTCN.
///
/// Staging: no pedestal and nothing moving in the background; one thing moves at a time, slowly, with eased starts
/// and stops. Models float at eye height or stand on the alcove floor, and a key light from in front gives every one a
/// floor shadow. Nothing is white or light (invisible on the HLD). Headlines are extruded 3D letters in the UTCN grey.
/// Assets: Resources/Story, made by SourceModels/Story/make_story_assets.py.
/// </summary>
public class StoryDirector : MonoBehaviour
{
    [Tooltip("Key light while the spots run, as a share of the alcove's Spot Light, which is dimmed meanwhile: the floor " +
             "is lit past white, and shadows only show where the light left over is below white.")]
    [SerializeField] private float keyLight = 1f;
    [SerializeField] private float alcoveSpotDuringStory = 0.3f;

    // Layout above the floor, world units. The showcase frames objects up to about 4.5 above it.
    private const float Mid = 1.55f;                                // where floating things sit
    private const float TextY = 3.65f, TextHeight = 0.62f, TextWidth = 2.5f, TextForward = 0.6f; // headlines: compact, with air round them
    private const int SwarmSize = 80;

    private ObjectSwitcher switcher;
    private DisplayAlignment display;
    private Light alcoveSpot;
    private float alcoveSpotIntensity;
    private bool running, dimmed;
    private Material dark, red;
    private Vector3 center; // on the floor, in the middle of the stage

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly Dictionary<GameObject, Vector3> fullScale = new Dictionary<GameObject, Vector3>();
    private readonly List<(GameObject go, float speed)> idle = new List<(GameObject, float)>();
    private GameObject heart, hovering;
    private float beatPhase;

    // ---------------------------------------------------------------- start, stop, keys

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        Attach();
        SceneManager.sceneLoaded += (s, m) => Attach();
    }

    private static void Attach()
    {
        var showcase = FindAnyObjectByType<ObjectSwitcher>();
        if (showcase != null && showcase.GetComponent<StoryDirector>() == null)
            showcase.gameObject.AddComponent<StoryDirector>();
    }

    private void Start()
    {
        switcher = GetComponent<ObjectSwitcher>();
        display = FindAnyObjectByType<DisplayAlignment>();
        dark = Resources.Load<Material>("Models/UTCN/UTCN_Dark");
        red = Resources.Load<Material>("Models/UTCN/UTCN_Red");
        var spot = GameObject.Find("Spot Light");
        alcoveSpot = spot != null ? spot.GetComponent<Light>() : null;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Play(false);
        else if (keyboard != null && keyboard.mKey.wasPressedThisFrame) Play(true);
        else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && running) Finish();
        if (!running) return;

        float dt = Time.deltaTime;
        if (heart != null) // lub-dub, 66 bpm
        {
            beatPhase = (beatPhase + dt * 66f / 60f) % 1f;
            float Bump(float at, float width) => Mathf.Exp(-Mathf.Pow((beatPhase - at) / width, 2f));
            Vector3 middle = Middle(heart);
            Scale(heart, 1f + 0.07f * Bump(0.08f, 0.05f) + 0.045f * Bump(0.3f, 0.05f));
            heart.transform.position += middle - Middle(heart);
        }
        foreach (var (go, speed) in idle)
            if (go != null) go.transform.RotateAround(Middle(go), Vector3.up, speed * dt);
        foreach (var (edge, a, b) in links)
            if (edge != null && a != null && b != null) Link(edge, a.position, b.position, linksGrown);
    }

    private void Play(bool health)
    {
        Stop();
        running = true;
        StartCoroutine(Run(health));
    }

    // Removes everything the spots made and gives the alcove its lighting back.
    private void Stop()
    {
        StopAllCoroutines();
        running = false;
        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();
        fullScale.Clear(); letters.Clear(); idle.Clear(); network.Clear(); links.Clear();
        heart = hovering = null;
        if (dimmed && alcoveSpot != null) alcoveSpot.intensity = alcoveSpotIntensity;
        dimmed = false;
    }

    private void OnDestroy() => Stop();

    // The end: the AIRI <-> UTCN logo loop (the showcase's first object); the showcase runs as usual from here.
    private void Finish()
    {
        Stop();
        switcher.HideAll();
        switcher.ShowFirst();
        switcher.enabled = true;
    }

    private IEnumerator Run(bool health)
    {
        switcher.enabled = false;
        yield return null; // after ObjectSwitcher.Start has shown its first object
        switcher.HideAll();
        if (display != null) display.HelpVisible = false;
        Measure();
        AddKeyLight();
        yield return health ? HealthSpot() : AiriSpot();
        Finish();
    }

    // ---------------------------------------------------------------- spot 1: AIRI x UTCN

    private IEnumerator AiriSpot()
    {
        float t0 = Time.time;
        IEnumerator At(float second) { while (Time.time - t0 < second) yield return null; }
        Vector3 mid = Floor(Mid);

        // The spark drifts in from deep in the alcove and comes to rest at eye height, breathing.
        var spark = Spark();
        StartCoroutine(Arrive(spark, center + new Vector3(1.5f, 4.0f, 2.2f), mid, 2.6f));
        yield return At(1.2f);
        var text = Phrase("Phrase_Spark");
        yield return At(3.0f);
        StartCoroutine(Hover(spark, mid));
        yield return At(5.8f);
        StartCoroutine(PhraseOut(text));

        // It learns: a neural network grows out of it and turns slowly; signals run along the links.
        yield return At(6.2f);
        var net = GrowNetwork(mid, new Vector3(0.8f, 0.5f, 0.55f), 22, 2.0f);
        idle.Add((net, 8f));
        yield return At(6.8f);
        text = Phrase("Phrase_Learns");
        yield return At(10.8f);
        StartCoroutine(PhraseOut(text));

        // The neurons settle onto a globe as city lights; the spark circles it.
        yield return At(11.2f);
        idle.RemoveAll(i => i.go == net);
        var globe = Hero("Models/Globe/Atlas", 1.1f, Mid - 0.55f);
        StartCoroutine(GrowFromMiddle(globe, 1.2f));
        StartCoroutine(NetworkOntoGlobe(globe, mid, 1.4f));
        idle.Add((globe, 16f));
        StartCoroutine(Circle(spark, mid, 0.85f, 5.0f));
        yield return At(11.9f);
        text = Phrase("Phrase_Connects");
        yield return At(16.2f);
        StartCoroutine(PhraseOut(text));

        // The world becomes a beating heart.
        yield return At(16.6f);
        heart = null;
        var newHeart = Hero("Models/HumanHeart/HumanHeart", 0.95f, Mid - 0.47f);
        yield return Morph(globe, newHeart, 1.6f);
        Remove(net);
        heart = newHeart;
        idle.Add((heart, 9f));
        StartCoroutine(Hover(spark, mid + new Vector3(0.95f, 0.35f, -0.2f)));
        yield return At(17.6f);
        text = Phrase("Phrase_Heart");
        yield return At(22.0f);
        StartCoroutine(PhraseOut(text));

        // AI meets people: the spark spirals into the heart and the two become the AIRI logo, on the floor.
        yield return At(22.4f);
        yield return Spiral(spark, Middle(heart), 1.5f);
        Remove(spark);
        var beating = heart;
        heart = null;
        var airi = Hero("Models/AIRI_Intro/AIRI_Intro", 1.1f, 0f, 0f, 2.2f);
        yield return Morph(beating, airi, 1.6f);
        StartCoroutine(Sway(airi, 18f, 0.5f, 5f));
        yield return At(24.4f);
        text = Phrase("Phrase_Meet");
        yield return At(28.8f);
        StartCoroutine(PhraseOut(text));

        // The logo pours down into a campus: the lawn spreads, the AIRI building assembles floor by floor, trees grow.
        yield return At(29.2f);
        var campus = BuildCampus(out var lab, out var plaza, out var props);
        StartCoroutine(Morph(airi, plaza, 1.2f, false));
        yield return At(30.0f);
        StartCoroutine(Assemble(lab, 2.2f));
        for (int i = 0; i < props.Count; i++) StartCoroutine(Delayed(1.0f + i * 0.14f, GrowFromBase(props[i], 0.7f)));
        yield return At(30.6f);
        text = Phrase("Phrase_Welcome");
        yield return At(32.0f);
        StartCoroutine(Sway(campus, 16f, 0.3f, 6.4f)); // a slow turn each way shows the curve of the front
        yield return At(33.4f);
        StartCoroutine(PhraseOut(text));
        yield return At(33.8f);
        text = Phrase("Phrase_Institute");
        yield return At(38.0f);
        StartCoroutine(PhraseOut(text));

        // Its departments and centres, a quick beat each: the last one flows into the next and it comes alive.
        GameObject previous = campus;
        float beat = 38.6f;
        foreach (var (phrase, build) in Departments())
        {
            yield return At(beat);
            var (stage, alive) = build();
            yield return Morph(previous, stage, 0.8f);
            if (alive != null) StartCoroutine(alive);
            text = Phrase(phrase, true);
            yield return At(beat + 2.45f);
            StartCoroutine(PhraseOut(text, true));
            previous = stage;
            beat += 2.8f;
        }

        // 5G-SHARE: a mast links four partner campuses across Europe.
        yield return At(66.6f);
        var (network5G, alive5G) = FiveGShare();
        yield return Morph(previous, network5G, 0.9f);
        StartCoroutine(alive5G);
        yield return At(67.4f);
        text = Phrase("Phrase_5G");
        yield return At(69.4f);
        StartCoroutine(PhraseOut(text));
        yield return At(69.8f);
        text = Phrase("Phrase_5GNet");
        yield return At(72.2f);
        StartCoroutine(PhraseOut(text));

        // It all becomes AIRI and UTCN.
        yield return At(72.6f);
        var logos = LogoPair(0.95f, 0.3f);
        yield return Morph(network5G, logos, 1.4f);
        StartCoroutine(Sway(logos, 10f, 0.3f, 10.2f));
        yield return At(73.4f);
        text = Phrase("Phrase_Future");
        yield return At(77.4f);
        StartCoroutine(PhraseOut(text));
        yield return At(77.8f);
        text = Phrase("Phrase_JoinUs");
        yield return At(82.0f);
        StartCoroutine(PhraseOut(text));
        yield return ShrinkToMiddle(logos, 0.9f);
    }

    // ---------------------------------------------------------------- the departments, a quick beat each

    // In order: the headline, and what to build (the stage to morph into, and what makes it come alive).
    private IEnumerable<(string, Func<(GameObject, IEnumerator)>)> Departments()
    {
        yield return ("Dept_Micro", () =>
        {
            var board = Hero("Story/Chip", 1.0f, Mid - 0.45f, 0f, 1.9f);
            board.transform.RotateAround(Middle(board), Vector3.right, -50f); // tipped toward the viewer, so its face shows
            return (board, ChipAlive(board));
        });
        yield return ("Dept_Robotics", () =>
        {
            var arm = Hero("Story/RobotArm", 1.35f, 0f, 0f, 1.4f);
            return (arm, ArmAlive(arm));
        });
        yield return ("Dept_Autonomous", () =>
        {
            var car = Hero("Story/Car", 0.8f, 0f, 25f, 1.6f);
            var drone = Hero("Story/Drone", 0.4f, 1.55f, 0f, 1.0f, center.x - 0.3f, center.z + 0.2f);
            return (Group("Autonomous", car, drone), Both(CarAlive(car), DroneAlive(drone)));
        });
        yield return ("Dept_Cyber", () =>
        {
            var padlock = Hero("Story/Padlock", 0.95f, Mid - 0.55f, 0f, 1.0f);
            var sat = Hero("Story/Satellite", 0.35f, Mid + 0.2f, 0f, 0.9f, center.x + 0.9f, center.z);
            return (Group("Cyber", padlock, sat), Both(Sway(padlock, 14f, 0.35f, 2.6f), CircleAround(sat, Middle(padlock), 0.95f)));
        });
        yield return ("Dept_Health", () =>
        {
            var retina = Hero("Models/W_I_OS/W_I_OS", 0.8f, Mid - 0.3f, 0f, 1.8f);
            var fluids = PrepareRetina(retina);
            idle.Add((retina, 14f));
            return (retina, Both(ScanBeam(retina, 1.3f), RevealAll(fluids, 0.7f)));
        });
        yield return ("Dept_Trust", () =>
        {
            var balance = Hero("Story/Scale", 1.25f, 0f, 0f, 1.3f);
            return (balance, Settle(balance));
        });
        yield return ("Dept_Measure", () =>
        {
            var gauge = Hero("Story/Gauge", 1.05f, 0f, 0f, 1.6f);
            return (gauge, Measure(gauge));
        });
        yield return ("Dept_Immersive", () =>
        {
            var vr = Hero("Story/VR", 1.0f, Mid - 0.45f, 0f, 1.9f);
            return (vr, PopIn(vr, "Panel", 0.25f, true));
        });
        yield return ("Dept_Labelling", () =>
        {
            var things = Hero("Story/Labelling", 0.6f, 0f, 0f, 1.7f);
            return (things, Both(PopIn(things, "Box", 0.3f, false), Delayed(0.25f, PopIn(things, "Tag", 0.3f, false))));
        });
        yield return ("Dept_HPC", () =>
        {
            var rack = Hero("Story/Rack", 1.5f, 0f, 0f, 1.0f);
            return (rack, Blink(rack, "Led"));
        });
    }

    // ---------------------------------------------------------------- 5G-SHARE

    // The mast in the middle and the four partner campuses round it (Ireland, Romania, Czech Republic, Italy), each
    // labelled with its country code.
    private (GameObject, IEnumerator) FiveGShare()
    {
        var tower = Hero("Story/Tower5G", 1.75f, 0f, 0f, 1.0f, center.x, center.z + 0.2f);
        var parts = new List<GameObject> { tower };
        var hubs = new List<GameObject>();
        string[] codes = { "IE", "RO", "CZ", "IT" };
        (float x, float z)[] spots = { (-1.15f, -0.55f), (1.15f, -0.55f), (-0.9f, 0.85f), (0.9f, 0.85f) }; // inside the alcove (half-width about 1.6)
        for (int i = 0; i < 4; i++)
        {
            var hub = Hero("Story/Hub", 0.42f, 0f, 0f, 0.62f, center.x + spots[i].x, center.z + spots[i].z);
            hubs.Add(hub);
            parts.Add(hub);
            var label = Load("Story/Code_" + codes[i], Quaternion.Euler(0f, 180f, 0f));
            var lb = GesturePlayer.GetBounds(Parts(label));
            label.transform.localScale *= 0.13f / lb.size.y;
            Stand(label, Top(hub) + Vector3.up * 0.06f);
            parts.Add(label);
        }
        var group = Group("5G-SHARE", parts.ToArray());
        return (group, FiveGAlive(group, tower, hubs));
    }

    // Links grow from the mast's antennas to each campus's antenna as arcs; data packets run along them both ways;
    // 5G waves spread from the antennas; the aviation light blinks.
    private IEnumerator FiveGAlive(GameObject group, GameObject tower, List<GameObject> hubs)
    {
        Transform beacon = FindChild(tower.transform, "Beacon");
        var sectors = new List<Transform>();
        for (int i = 0; i < 3; i++) { var t = FindChild(tower.transform, "Sector" + i); if (t != null) sectors.Add(t); }
        Vector3 Source()
        {
            Vector3 sum = Vector3.zero;
            foreach (var t in sectors) sum += t.GetComponent<Renderer>().bounds.center;
            return sectors.Count > 0 ? sum / sectors.Count : Top(tower);
        }
        var ends = hubs.ConvertAll(h => FindChild(h.transform, "Antenna"));
        var lines = new List<LineRenderer>();
        foreach (var _ in hubs)
        {
            var go = new GameObject("Link");
            go.transform.SetParent(group.transform, true);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = red;
            line.widthMultiplier = 0.012f;
            line.positionCount = 24;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lines.Add(line);
        }
        Vector3 Arc(Vector3 a, Vector3 b, float t)
        {
            Vector3 top = (a + b) * 0.5f + Vector3.up * (0.35f + 0.15f * (a - b).magnitude);
            float u = 1f - t;
            return u * u * a + 2f * u * t * top + t * t * b;
        }
        Vector3 End(int i) => ends[i] != null ? ends[i].GetComponent<Renderer>().bounds.max - Vector3.up * 0.01f : Top(hubs[i]);
        float nextPacket = 0f, nextWave = 0.2f;
        var rnd = new System.Random(15);
        for (float time = 0f; group != null && tower != null; time += Time.deltaTime)
        {
            Vector3 a = Source();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null || hubs[i] == null) continue;
                float grown = Smooth(Mathf.Clamp01((time - i * 0.15f) / 0.7f));   // each link reaches out in turn
                for (int k = 0; k < lines[i].positionCount; k++)
                    lines[i].SetPosition(k, Arc(a, End(i), grown * k / (lines[i].positionCount - 1f)));
            }
            if (time > 0.8f && time >= nextPacket)
            {
                nextPacket = time + 0.09f;
                int i = rnd.Next(hubs.Count);
                bool outward = rnd.Next(3) > 0;
                StartCoroutine(Packet(group, () => Source(), () => End(i), outward, Arc));
            }
            if (time >= nextWave)
            {
                nextWave = time + 0.75f;
                var ring = Load("Story/Ring", Quaternion.identity);
                ring.transform.SetParent(group.transform, true);
                Vector3 at = a;
                StartCoroutine(Tween(1.3f, t => { if (ring != null) { ring.transform.position = at; ring.transform.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Lerp(0.15f, 1.6f, t) * (1f - t * t)); } }));
                StartCoroutine(Delayed(1.35f, Drop(ring)));
            }
            if (beacon != null) beacon.GetComponent<Renderer>().sharedMaterial = (time % 1f) < 0.5f ? red : dark;
            yield return null;
        }
    }

    // A small red packet runs along a link from the mast to a campus, or back.
    private IEnumerator Packet(GameObject group, Func<Vector3> from, Func<Vector3> to, bool outward, Func<Vector3, Vector3, float, Vector3> arc)
    {
        var dot = Primitive(PrimitiveType.Sphere, "Packet", red);
        dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dot.transform.localScale = Vector3.one * 0.03f;
        dot.transform.SetParent(group.transform, true);
        yield return Tween(0.7f, t =>
        {
            if (dot == null || group == null) return;
            float k = Smooth(t);
            dot.transform.position = arc(from(), to(), outward ? k : 1f - k);
        });
        Remove(dot);
    }

    // Several models as one stage, so a morph can take them all at once.
    private GameObject Group(string name, params GameObject[] parts)
    {
        var root = new GameObject(name);
        root.transform.position = center;
        spawned.Add(root);
        foreach (var go in parts) go.transform.SetParent(root.transform, true);
        return root;
    }

    private IEnumerator Both(IEnumerator a, IEnumerator b)
    {
        StartCoroutine(a);
        yield return b;
    }

    private IEnumerator RevealAll(List<GameObject> fluids, float after)
    {
        yield return new WaitForSeconds(after);
        for (int i = 0; i < fluids.Count; i++) StartCoroutine(Delayed(i * 0.2f, Reveal(fluids[i])));
    }

    // Goes round `middle`, tilted a little, and keeps going.
    private IEnumerator CircleAround(GameObject go, Vector3 middle, float radius)
    {
        Vector3 start = go.transform.position;
        for (float time = 0f; go != null; time += Time.deltaTime)
        {
            float a = time * 1.6f;
            Vector3 on = middle + Quaternion.Euler(0f, 0f, 15f) * new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            go.transform.position = Vector3.Lerp(start, on, Smooth(Mathf.Min(1f, time * 2f)));
            go.transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            yield return null;
        }
    }

    // The beam tips and swings, then settles level: fair, balanced.
    private IEnumerator Settle(GameObject balance)
    {
        var beam = FindChild(balance.transform, "Beam");
        if (beam == null) yield break;
        Quaternion rest = beam.localRotation;
        Vector3 axis = beam.parent.InverseTransformDirection(Vector3.forward);
        for (float time = 0f; beam != null; time += Time.deltaTime)
        {
            beam.localRotation = Quaternion.AngleAxis(14f * Mathf.Cos(time * 4.5f) * Mathf.Exp(-time * 1.3f), axis) * rest;
            yield return null;
        }
    }

    // The needle sweeps up the dial while the chart's bars grow one after another.
    private IEnumerator Measure(GameObject gauge)
    {
        var needle = FindChild(gauge.transform, "Needle");
        var bars = new List<Transform>();
        for (int i = 0; i < 5; i++) { var b = FindChild(gauge.transform, "Bar" + i); if (b != null) bars.Add(b); }
        var heights = bars.ConvertAll(b => b.localScale);
        foreach (var b in bars) b.localScale = Vector3.Scale(b.localScale, new Vector3(1f, 0.001f, 1f));
        Quaternion rest = needle != null ? needle.localRotation : Quaternion.identity;
        Vector3 axis = needle != null ? needle.parent.InverseTransformDirection(Vector3.forward) : Vector3.forward;
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i]; var full = heights[i];
            StartCoroutine(Delayed(0.15f + i * 0.18f, Tween(0.5f, t => { if (bar != null) bar.localScale = Vector3.Scale(full, new Vector3(1f, Mathf.Max(0.001f, EaseOutBack(t)), 1f)); })));
        }
        if (needle != null)
            yield return Tween(1.6f, t => { if (needle != null) needle.localRotation = Quaternion.AngleAxis(Mathf.Lerp(120f, -100f, EaseOutBack(t)), axis) * rest; });
    }

    // Parts named <prefix>0, <prefix>1, ... start hidden and pop in one after another; `bob` keeps them floating.
    private IEnumerator PopIn(GameObject go, string prefix, float step, bool bob)
    {
        var parts = new List<Transform>();
        for (int i = 0; i < 9; i++) { var t = FindChild(go.transform, prefix + i); if (t != null) parts.Add(t); }
        var sizes = parts.ConvertAll(t => t.localScale);
        var homes = parts.ConvertAll(t => t.localPosition);
        foreach (var t in parts) t.localScale = Vector3.one * 0.001f;
        for (int i = 0; i < parts.Count; i++)
        {
            var t = parts[i]; var size = sizes[i];
            StartCoroutine(Delayed(i * step, Tween(0.45f, k => { if (t != null) t.localScale = size * Mathf.Max(0.001f, EaseOutBack(k)); })));
        }
        if (!bob) yield break;
        for (float time = 0f; go != null; time += Time.deltaTime)
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null) parts[i].localPosition = homes[i] + parts[i].parent.InverseTransformVector(Vector3.up * 0.04f * Mathf.Sin(time * 2f + i));
            yield return null;
        }
    }

    // Status LEDs flicker red, at random, as the machines work.
    private IEnumerator Blink(GameObject go, string prefix)
    {
        var leds = new List<Renderer>();
        foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.name.StartsWith(prefix)) leds.Add(r);
        var rnd = new System.Random(9);
        while (go != null && leds.Count > 0)
        {
            foreach (var led in leds) if (led != null) led.sharedMaterial = rnd.Next(3) == 0 ? red : dark;
            yield return new WaitForSeconds(0.09f);
        }
    }

    // Signals leave the chip along its traces, one after another, to the edge of the board.
    private IEnumerator ChipAlive(GameObject chip)
    {
        var traces = new List<Renderer>();
        foreach (var r in chip.GetComponentsInChildren<Renderer>())
            if (r.name.StartsWith("Trace")) traces.Add(r);
        var rnd = new System.Random(3);
        while (chip != null && traces.Count > 0)
        {
            var b = traces[rnd.Next(traces.Count)].bounds;
            Vector3 mid = Middle(chip), axis = b.size.x > b.size.z ? Vector3.right * b.extents.x : Vector3.forward * b.extents.z;
            if (b.size.y > Mathf.Max(b.size.x, b.size.z)) axis = Vector3.up * b.extents.y; // tipped: the long side may be vertical
            Vector3 a = b.center - axis, c = b.center + axis;
            if ((a - mid).sqrMagnitude > (c - mid).sqrMagnitude) (a, c) = (c, a); // from the chip outward
            StartCoroutine(Travel(a, c, 0.6f, 0.035f));
            yield return new WaitForSeconds(0.14f);
        }
    }

    private IEnumerator Travel(Vector3 from, Vector3 to, float seconds, float size)
    {
        var dot = Primitive(PrimitiveType.Sphere, "Signal", red);
        dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dot.transform.localScale = Vector3.one * size;
        yield return Tween(seconds, t => { if (dot != null) dot.transform.position = Vector3.Lerp(from, to, Smooth(t)); });
        Remove(dot);
    }

    // The arm reaches down to the crate, grips it, lifts it, swings it round and sets it down.
    private IEnumerator ArmAlive(GameObject arm)
    {
        Transform Part(string n) => FindChild(arm.transform, n);
        Transform turret = Part("Turret"), upper = Part("UpperArm"), fore = Part("Forearm"), wrist = Part("Wrist"),
                  fingerL = Part("FingerL"), fingerR = Part("FingerR"), crate = Part("Crate"), basePart = Part("Base");
        if (turret == null || upper == null || fore == null || wrist == null) yield break;
        // The file keeps every part flat under its root (see make_story_assets.py); chain them here.
        turret.SetParent(basePart != null ? basePart : turret.parent, true);
        upper.SetParent(turret, true);
        fore.SetParent(upper, true);
        wrist.SetParent(fore, true);
        if (fingerL != null) fingerL.SetParent(wrist, true);
        if (fingerR != null) fingerR.SetParent(wrist, true);

        var restT = turret.localRotation; var restU = upper.localRotation; var restF = fore.localRotation;
        Vector3 yawAxis = turret.parent.InverseTransformDirection(Vector3.up);
        Vector3 pitchU = upper.parent.InverseTransformDirection(Vector3.right), pitchF = fore.parent.InverseTransformDirection(Vector3.right);
        // Bend the way that brings the hand down toward the crate (the file's axes are not worth guessing).
        float Toward(Transform joint, Vector3 axis, Quaternion rest)
        {
            if (crate == null) return 1f;
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
        void Pose(float yaw, float shoulder, float elbow, float close)
        {
            if (turret == null) return;
            turret.localRotation = Quaternion.AngleAxis(yaw, yawAxis) * restT;
            upper.localRotation = Quaternion.AngleAxis(su * shoulder, pitchU) * restU;
            fore.localRotation = Quaternion.AngleAxis(sf * elbow, pitchF) * restF;
            if (fingerL != null) fingerL.localPosition = lRest + grip * close;
            if (fingerR != null) fingerR.localPosition = rRest - grip * close;
        }
        yield return Tween(0.9f, t => Pose(0f, 28f * Smooth(t), 22f * Smooth(t), 0f));   // reach down
        yield return Tween(0.3f, t => Pose(0f, 28f, 22f, Smooth(t)));                      // grip
        if (crate != null && wrist != null) crate.SetParent(wrist, true);
        yield return Tween(0.8f, t => Pose(0f, 28f * (1f - Smooth(t)), 22f * (1f - Smooth(t)), 1f)); // lift
        yield return Tween(1.0f, t => Pose(110f * Smooth(t), 0f, 0f, 1f));                 // swing round
        yield return Tween(0.6f, t => Pose(110f, 20f * Smooth(t), 14f * Smooth(t), 1f));   // set it down
        yield return Tween(0.3f, t => Pose(110f, 20f, 14f, 1f - Smooth(t)));               // let go
    }

    // Hovers, drifting left and right and leaning into each move, rotors spinning.
    private IEnumerator DroneAlive(GameObject drone)
    {
        var rotors = new List<Transform>();
        foreach (var t in drone.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Rotor")) rotors.Add(t);
        Vector3 home = drone.transform.position;
        Quaternion level = drone.transform.rotation;
        for (float time = 0f; drone != null; time += Time.deltaTime)
        {
            float drift = 0.35f * Mathf.Sin(time * 1.3f), lean = -9f * Mathf.Cos(time * 1.3f);
            drone.transform.SetPositionAndRotation(home + new Vector3(drift, 0.05f * Mathf.Sin(time * 2.6f), 0f),
                                                   Quaternion.AngleAxis(lean, Vector3.forward) * level);
            foreach (var r in rotors) if (r != null) r.Rotate(drone.transform.up, 1400f * Time.deltaTime, Space.World);
            yield return null;
        }
    }

    // Turns slowly on the spot, wheels rolling, the lidar spinning and sending scan rings across the floor.
    private IEnumerator CarAlive(GameObject car)
    {
        var wheels = new List<Transform>();
        Transform lidar = null;
        foreach (var t in car.GetComponentsInChildren<Transform>())
        {
            if (t.name.StartsWith("Wheel")) wheels.Add(t);
            if (t.name == "Lidar") lidar = t;
        }
        float nextRing = 0f;
        for (float time = 0f; car != null; time += Time.deltaTime)
        {
            car.transform.RotateAround(Middle(car), Vector3.up, 22f * Time.deltaTime);
            foreach (var w in wheels) if (w != null) w.Rotate(car.transform.right, 260f * Time.deltaTime, Space.World);
            if (lidar != null) lidar.Rotate(car.transform.up, 500f * Time.deltaTime, Space.World);
            if (lidar != null && time >= nextRing)
            {
                nextRing = time + 0.9f;
                var ring = Load("Story/Ring", Quaternion.identity);
                ring.transform.position = new Vector3(lidar.position.x, center.y + 0.02f, lidar.position.z);
                StartCoroutine(Tween(1.2f, t => { if (ring != null) ring.transform.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Lerp(0.2f, 1.3f, t) * (1f - t * t)); }));
                StartCoroutine(Delayed(1.25f, Drop(ring)));
            }
            yield return null;
        }
    }

    private IEnumerator Drop(GameObject go)
    {
        Remove(go);
        yield break;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // ---------------------------------------------------------------- spot 2: AI for health

    private IEnumerator HealthSpot()
    {
        float t0 = Time.time;
        IEnumerator At(float second) { while (Time.time - t0 < second) yield return null; }

        var retina = Hero("Models/W_I_OS/W_I_OS", 0.8f, Mid - 0.3f, 0f, 1.8f);
        var fluids = PrepareRetina(retina);
        StartCoroutine(GrowFromMiddle(retina, 1.4f));
        idle.Add((retina, 10f));
        yield return At(0.6f);
        var text = Phrase("Phrase_Health");
        yield return At(3.8f);
        StartCoroutine(PhraseOut(text));

        // A scan beam passes through it; the fluid pockets show and pulse.
        yield return At(4.2f);
        StartCoroutine(ScanBeam(retina, 2.6f));
        yield return At(5.0f);
        text = Phrase("Phrase_Sees");
        for (int i = 0; i < fluids.Count; i++) StartCoroutine(Delayed(1.0f + i * 0.35f, Reveal(fluids[i])));
        yield return At(9.8f);
        StartCoroutine(PhraseOut(text));
        yield return At(10.2f);
        text = Phrase("Phrase_Sight");
        yield return At(15.0f);
        StartCoroutine(PhraseOut(text));

        // It becomes the AIRI and UTCN logos, side by side.
        yield return At(15.4f);
        idle.RemoveAll(i => i.go == retina);
        var logos = LogoPair(0.9f, 0.25f);
        yield return Morph(retina, logos, 1.6f);
        StartCoroutine(Sway(logos, 10f, 0.4f, 5.5f));
        yield return At(17.4f);
        text = Phrase("Phrase_AIRIxUTCN");
        yield return At(21.4f);
        StartCoroutine(PhraseOut(text));
        yield return ShrinkToMiddle(logos, 0.9f);
    }

    // The scan's grey tissue shell reads as fog on the HLD: off. The B-scan surfaces stay; the fluids wait, hidden.
    private List<GameObject> PrepareRetina(GameObject retina)
    {
        foreach (var mb in retina.GetComponentsInChildren<MonoBehaviour>())
            if (mb.GetType().Name == "OctScanControls") mb.enabled = false; // its keys and info box are for the showcase
        var fluids = new List<GameObject>();
        foreach (var r in retina.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name;
            if (n == "retinal tissue") r.enabled = false;
            else if (n == "Cyst / IRF" || n == "SRF" || n == "PED")
            {
                r.enabled = true;
                fluids.Add(r.gameObject);
                fullScale[r.gameObject] = r.transform.localScale;
                r.transform.localScale = Vector3.one * 0.001f;
            }
        }
        return fluids;
    }

    // A fluid pocket grows in, then keeps pulsing gently so the eye goes to it.
    private IEnumerator Reveal(GameObject fluid)
    {
        yield return Tween(0.8f, t => Scale(fluid, EaseOutBack(t)));
        for (float time = 0f; fluid != null; time += Time.deltaTime)
        {
            Scale(fluid, 1f + 0.06f * Mathf.Sin(time * 4f));
            yield return null;
        }
    }

    // A thin red sheet of light passes from the back of the scan to the front.
    private IEnumerator ScanBeam(GameObject target, float seconds)
    {
        Bounds b = GesturePlayer.GetBounds(Parts(target));
        var beam = Primitive(PrimitiveType.Cube, "Scan Beam", red);
        beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        yield return Tween(seconds, t =>
        {
            beam.transform.position = new Vector3(b.center.x, b.center.y, Mathf.Lerp(b.max.z, b.min.z, Smooth(t)));
            beam.transform.localScale = new Vector3(b.size.x * 1.1f, Mathf.Max(0.001f, b.size.y * 1.3f * Mathf.Sin(Mathf.PI * t)), 0.01f);
        });
        Remove(beam);
    }

    // ---------------------------------------------------------------- stage and light

    private void Measure()
    {
        float depth = PlayerPrefs.GetFloat("HLD_ObjectDepth", -1.5f); // GesturePlayer's shared W / S depth
        Vector3 spot = switcher.Spot;
        float floor = Physics.Raycast(spot + Vector3.up * 5f, Vector3.down, out var hit, 50f) ? hit.point.y : spot.y - 1f;
        center = new Vector3(spot.x, floor, spot.z + depth);
    }

    private Vector3 Floor(float height) => center + Vector3.up * height;

    // A copy of the alcove's Spot Light from in front and above, so each model throws its whole shape onto the floor.
    private void AddKeyLight()
    {
        if (alcoveSpot == null) return;
        alcoveSpotIntensity = alcoveSpot.intensity; // read now: the logo loop dims it while it shows, and it was just hidden
        var go = Instantiate(alcoveSpot.gameObject);
        go.name = "Story Key Light";
        spawned.Add(go);
        go.transform.position = center + new Vector3(-0.8f, 3.8f, -3.0f);
        go.transform.rotation = Quaternion.LookRotation(center + Vector3.up * 1.2f - go.transform.position);
        var light = go.GetComponent<Light>();
        light.intensity = alcoveSpotIntensity * keyLight;
        light.shadows = LightShadows.Soft;
        alcoveSpot.intensity = alcoveSpotIntensity * alcoveSpotDuringStory;
        dimmed = true;
    }

    // ---------------------------------------------------------------- the swarm: one image turns into the next

    // `from` breaks up into particles on its outline, they stream across in staggered curves and gather on `to`'s
    // outline, and `to` grows out of them. `to` is already in place at full size; `removeFrom` drops `from` afterwards.
    private IEnumerator Morph(GameObject from, GameObject to, float seconds, bool removeFrom = true)
    {
        Bounds a = GesturePlayer.GetBounds(Parts(from)), b = GesturePlayer.GetBounds(Parts(to));
        Scale(to, 0f);
        var rnd = new System.Random(from.GetHashCode());
        Vector3 OnShell(Bounds box)
        {
            var d = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1).normalized;
            return box.center + Vector3.Scale(d, box.extents * 0.85f);
        }
        var dots = new List<(Transform t, Vector3 p0, Vector3 bend, Vector3 p1, float delay, float size)>();
        for (int i = 0; i < SwarmSize; i++)
        {
            var dot = Primitive(PrimitiveType.Sphere, "Swarm", i % 4 == 0 ? red : dark);
            dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Vector3 p0 = OnShell(a), p1 = OnShell(b);
            Vector3 bend = Vector3.Lerp(p0, p1, 0.5f) + new Vector3((float)rnd.NextDouble() - 0.5f, 0.4f + (float)rnd.NextDouble() * 0.4f, (float)rnd.NextDouble() - 0.5f) * 0.6f;
            dots.Add((dot.transform, p0, bend, p1, (float)rnd.NextDouble() * 0.3f, 0.025f + (float)rnd.NextDouble() * 0.025f));
            dot.transform.position = p0;
            dot.transform.localScale = Vector3.one * 0.001f;
        }
        Vector3 fromMiddle = a.center, toMiddle = b.center;
        yield return Tween(seconds, t =>
        {
            float gone = Mathf.Clamp01(t / 0.3f), grown = Mathf.Clamp01((t - 0.65f) / 0.35f);
            if (from != null)
            {
                Scale(from, 1f - Smooth(gone));
                from.transform.position += fromMiddle - Middle(from);
            }
            Scale(to, EaseOutBack(grown));
            to.transform.position += toMiddle - Middle(to);
            foreach (var (dot, p0, bend, p1, delay, size) in dots)
            {
                float k = Smooth(Mathf.Clamp01((t - delay) / 0.7f)), e = 1f - k;
                dot.position = e * e * p0 + 2f * e * k * bend + k * k * p1;
                float alive = Mathf.Min(Mathf.Clamp01(t / 0.15f), Mathf.Clamp01((1f - t) / 0.2f)); // grow in, shrink out
                dot.localScale = Vector3.one * Mathf.Max(0.001f, size * alive);
            }
        });
        foreach (var d in dots) Remove(d.t.gameObject);
        if (removeFrom) Remove(from);
        else if (from != null) from.SetActive(false);
    }

    // ---------------------------------------------------------------- the spark

    private GameObject Spark()
    {
        var spark = Primitive(PrimitiveType.Sphere, "Spark", red);
        spark.transform.localScale = Vector3.one * 0.15f;
        var trail = spark.AddComponent<TrailRenderer>();
        trail.time = 0.6f;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 0.12f, 1f, 0f);
        trail.sharedMaterial = red;
        return spark;
    }

    // Glides from `from` to `to` on a curve that swings toward the viewer, slowing to a stop.
    private IEnumerator Arrive(GameObject spark, Vector3 from, Vector3 to, float seconds)
    {
        Vector3 bend = new Vector3(to.x - 1.4f, (from.y + to.y) * 0.5f, to.z - 1.2f);
        yield return Tween(seconds, t =>
        {
            float e = 1f - Mathf.Pow(1f - t, 3f), u = 1f - e; // fast in, slow to rest
            if (spark != null) spark.transform.position = u * u * from + 2f * u * e * bend + e * e * to;
        });
    }

    // Floats at `at` and breathes until something else moves it; eases over from where it is.
    private IEnumerator Hover(GameObject spark, Vector3 at)
    {
        hovering = spark;
        Vector3 start = spark.transform.position, size = Vector3.one * 0.15f;
        for (float time = 0f; spark != null && hovering == spark; time += Time.deltaTime)
        {
            Vector3 here = at + new Vector3(0f, 0.04f * Mathf.Sin(time * 1.8f), 0f);
            spark.transform.position = Vector3.Lerp(start, here, Smooth(Mathf.Min(1f, time / 0.8f)));
            spark.transform.localScale = size * (1f + 0.12f * Mathf.Sin(time * 3.2f));
            yield return null;
        }
    }

    // One slow loop round `middle`, tilted so it passes in front of and behind it, easing on and off the loop.
    private IEnumerator Circle(GameObject spark, Vector3 middle, float radius, float seconds)
    {
        hovering = null;
        Vector3 start = spark.transform.position;
        yield return Tween(seconds, t =>
        {
            if (spark == null) return;
            float a = -Mathf.PI / 2f + Smooth(t) * Mathf.PI * 2f, r = radius * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.02f));
            Vector3 on = middle + Quaternion.Euler(0f, 0f, 18f) * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            spark.transform.position = Vector3.Lerp(start, on, Smooth(Mathf.Min(1f, t * 5f)));
        });
    }

    // Two closing loops round `target`, easing onto the spiral, and in.
    private IEnumerator Spiral(GameObject spark, Vector3 target, float seconds)
    {
        hovering = null;
        Vector3 start = spark.transform.position;
        yield return Tween(seconds, t =>
        {
            if (spark == null) return;
            float a = t * 4f * Mathf.PI, r = 0.9f * (1f - t * t);
            Vector3 on = target + new Vector3(Mathf.Cos(a) * r, 0.25f * Mathf.Sin(a * 0.5f) * (1f - t), Mathf.Sin(a) * r);
            spark.transform.position = Vector3.Lerp(start, on, Smooth(Mathf.Min(1f, t * 5f)));
        });
    }

    // ---------------------------------------------------------------- the neural network

    private readonly List<Transform> network = new List<Transform>();
    private readonly List<(Transform edge, Transform a, Transform b)> links = new List<(Transform, Transform, Transform)>();
    private float linksGrown;
    private bool networkLive;

    // Neurons pop in from the middle outward under one root (so it can turn slowly as a whole) and their links grow;
    // signals run along the links and light the neurons they reach.
    private GameObject GrowNetwork(Vector3 middle, Vector3 radii, int count, float seconds)
    {
        network.Clear(); links.Clear();
        var root = new GameObject("Network");
        root.transform.position = middle;
        spawned.Add(root);
        var rnd = new System.Random(11);
        var points = new List<Vector3>();
        while (points.Count < count)
        {
            var p = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1);
            if (p.sqrMagnitude <= 1f) points.Add(middle + Vector3.Scale(p, radii));
        }
        points.Sort((p, q) => (p - middle).sqrMagnitude.CompareTo((q - middle).sqrMagnitude));
        foreach (var p in points)
        {
            var node = Primitive(PrimitiveType.Sphere, "Neuron", dark);
            node.transform.position = p;
            node.transform.SetParent(root.transform, true);
            fullScale[node] = Vector3.one * 0.07f;
            node.transform.localScale = Vector3.one * 0.001f;
            network.Add(node.transform);
        }
        var linked = new HashSet<(int, int)>();
        for (int i = 0; i < count; i++) // each neuron links to its two nearest
        {
            var near = new List<int>();
            for (int j = 0; j < count; j++) if (j != i) near.Add(j);
            near.Sort((x, y) => (points[x] - points[i]).sqrMagnitude.CompareTo((points[y] - points[i]).sqrMagnitude));
            foreach (int j in near.GetRange(0, 2))
            {
                if (!linked.Add((Mathf.Min(i, j), Mathf.Max(i, j)))) continue;
                var edge = Primitive(PrimitiveType.Cylinder, "Synapse", dark);
                edge.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                edge.transform.SetParent(root.transform, true);
                links.Add((edge.transform, network[i], network[j]));
            }
        }
        linksGrown = 0f;
        networkLive = true;
        StartCoroutine(Tween(seconds, t =>
        {
            for (int i = 0; i < network.Count; i++)
                Scale(network[i].gameObject, EaseOutBack(Mathf.Clamp01(t * 1.6f - i / (float)network.Count * 0.6f)));
            linksGrown = Smooth(Mathf.Clamp01(t * 1.4f - 0.3f));
        }));
        StartCoroutine(Signals());
        return root;
    }

    // A thin cylinder from `a` toward `b`, `grown` of the way.
    private static void Link(Transform edge, Vector3 a, Vector3 b, float grown)
    {
        Vector3 d = (b - a) * grown;
        edge.position = a + d * 0.5f;
        edge.rotation = d.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.up, d) : Quaternion.identity;
        Vector3 parentScale = edge.parent != null ? edge.parent.lossyScale : Vector3.one;
        edge.localScale = new Vector3(0.011f / parentScale.x, Mathf.Max(0.0005f, d.magnitude * 0.5f) / parentScale.y, 0.011f / parentScale.z);
    }

    private IEnumerator Signals()
    {
        var rnd = new System.Random(5);
        while (networkLive && links.Count > 0)
        {
            var (_, a, b) = links[rnd.Next(links.Count)];
            if (rnd.Next(2) == 0) (a, b) = (b, a);
            StartCoroutine(Signal(a, b));
            yield return new WaitForSeconds(0.2f);
        }
    }

    private IEnumerator Signal(Transform from, Transform to)
    {
        var dot = Primitive(PrimitiveType.Sphere, "Signal", red);
        dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dot.transform.localScale = Vector3.one * 0.032f;
        yield return Tween(0.5f, t =>
        {
            if (from != null && to != null && dot != null) dot.transform.position = Vector3.Lerp(from.position, to.position, Smooth(t));
        });
        Remove(dot);
        if (to == null || !networkLive) yield break;
        var neuron = to.GetComponent<Renderer>(); // glows red for a moment when a signal arrives
        neuron.sharedMaterial = red;
        yield return new WaitForSeconds(0.3f);
        if (neuron != null && networkLive) neuron.sharedMaterial = dark;
    }

    // The links fade and every neuron drifts out onto the globe's surface, turning red, and turns with it like a city light.
    private IEnumerator NetworkOntoGlobe(GameObject globe, Vector3 middle, float seconds)
    {
        networkLive = false;
        var from = network.ConvertAll(n => n.position);
        float grown = linksGrown;
        const float radius = 0.565f;
        yield return Tween(seconds, t =>
        {
            float e = Smooth(t);
            linksGrown = grown * (1f - e);
            for (int i = 0; i < network.Count; i++)
            {
                if (network[i] == null) continue;
                network[i].position = Vector3.Lerp(from[i], middle + (from[i] - middle).normalized * radius, e);
                network[i].GetComponent<Renderer>().sharedMaterial = red;
                Scale(network[i].gameObject, Mathf.Lerp(1f, 0.55f, e));
            }
        });
        foreach (var (edge, _, _) in links) if (edge != null) Remove(edge.gameObject);
        links.Clear();
        foreach (var node in network) if (node != null && globe != null) node.SetParent(globe.transform, true);
    }

    // ---------------------------------------------------------------- the campus

    // A lawn, the AIRI building behind the middle, trees and lamps; all under one root so it can turn as one.
    private GameObject BuildCampus(out GameObject lab, out GameObject plaza, out List<GameObject> props)
    {
        var campus = new GameObject("Campus");
        campus.transform.position = center;
        spawned.Add(campus);
        plaza = Load("Story/Plaza", Quaternion.Euler(0f, 180f, 0f));
        plaza.transform.localScale *= 1.55f;
        plaza.transform.position = Floor(0.078f);
        lab = Hero("Story/AIRI_Lab", 2.0f, 0.078f, 0f, 2.9f, center.x, center.z + 0.35f);
        props = new List<GameObject>();
        foreach (var (x, z, s) in new[] { (-1.2f, -0.55f, 0.95f), (1.15f, -0.7f, 0.85f), (-0.7f, -1.05f, 0.75f), (0.75f, -1.1f, 0.8f),
                                          (-1.38f, 0.1f, 0.85f), (1.4f, -0.05f, 0.9f), (-0.95f, -1.25f, 0.6f) })
            props.Add(Hero("Story/Tree", 0.7f * s, 0.078f, (x * 47f + 360f) % 360f, null, center.x + x, center.z + z));
        props.Add(Hero("Story/Lamp", 0.45f, 0.078f, 180f, null, center.x - 0.28f, center.z - 0.9f)); // arms over the path
        props.Add(Hero("Story/Lamp", 0.45f, 0.078f, 0f, null, center.x + 0.28f, center.z - 0.9f));
        foreach (var go in new List<GameObject>(props) { lab, plaza }) go.transform.SetParent(campus.transform, true);
        foreach (var go in props) Scale(go, 0f);
        foreach (var part in Floors(lab)) part.gameObject.SetActive(false);
        return campus;
    }

    // The building's storeys F0..F6, then the roof, then the door (made by make_story_assets.py lab()).
    private static List<Transform> Floors(GameObject lab)
    {
        var parts = new List<Transform>();
        foreach (var t in lab.GetComponentsInChildren<Transform>(true))
            if ((t.name.Length == 2 && t.name[0] == 'F' && char.IsDigit(t.name[1])) || t.name == "Roof" || t.name == "Door") parts.Add(t);
        parts.Sort((p, q) => Order(p.name).CompareTo(Order(q.name)));
        return parts;
    }

    private static int Order(string name) => name == "Door" ? 0 : name == "Roof" ? 99 : name[1] - '0';

    // Storey by storey from the ground up, each one settling down into place from a little above.
    private IEnumerator Assemble(GameObject lab, float seconds)
    {
        var parts = Floors(lab);
        float step = seconds / Mathf.Max(1, parts.Count);
        foreach (var part in parts)
        {
            part.gameObject.SetActive(true);
            Vector3 rest = part.localPosition, scale = part.localScale;
            Vector3 lift = part.parent.InverseTransformVector(Vector3.up * 0.35f);
            StartCoroutine(Tween(0.6f, t =>
            {
                if (part == null) return;
                part.localPosition = Vector3.LerpUnclamped(rest + lift, rest, EaseOutBack(t));
                float plan = Mathf.Lerp(0.8f, 1f, Smooth(t)); // widens a little as it settles (it grows out of its own plan)
                part.localScale = Vector3.Scale(scale, new Vector3(plan, 1f, plan));
            }));
            yield return new WaitForSeconds(step);
        }
    }

    // The reverse: top first, each storey lifts off and folds away; then the trees and the lawn.
    private IEnumerator Disassemble(GameObject campus, GameObject lab, GameObject plaza, List<GameObject> props, float seconds)
    {
        var parts = Floors(lab);
        parts.Reverse();
        foreach (var go in props) StartCoroutine(Tween(0.5f, t => Scale(go, 1f - Smooth(t))));
        float step = seconds * 0.6f / Mathf.Max(1, parts.Count);
        foreach (var part in parts)
        {
            Vector3 rest = part.localPosition, scale = part.localScale;
            Vector3 lift = part.parent.InverseTransformVector(Vector3.up * 0.3f);
            StartCoroutine(Tween(0.45f, t =>
            {
                if (part == null) return;
                part.localPosition = rest + lift * t * t;
                float plan = Mathf.Max(0.001f, 1f - Smooth(t));
                part.localScale = Vector3.Scale(scale, new Vector3(plan, 1f, plan));
            }));
            yield return new WaitForSeconds(step);
        }
        Vector3 full = plaza.transform.localScale;
        yield return Tween(seconds * 0.4f, t => plaza.transform.localScale = new Vector3(full.x * Mathf.Max(0.001f, 1f - Smooth(t)), full.y, full.z * Mathf.Max(0.001f, 1f - Smooth(t))));
    }

    // Turns gently one way and the other around its own middle, `amplitude` degrees, one swing every 1/`speed` cycles.
    private IEnumerator Sway(GameObject go, float amplitude, float speed, float seconds)
    {
        Quaternion start = go.transform.rotation;
        Vector3 pivot = go.name == "Campus" ? go.transform.position : Middle(go);
        float last = 0f;
        for (float time = 0f; time < seconds && go != null; time += Time.deltaTime)
        {
            float fade = Mathf.Min(1f, time / 1.5f) * Mathf.Min(1f, (seconds - time) / 1.5f);
            float angle = amplitude * Mathf.Sin(time * speed * 2f * Mathf.PI) * fade;
            go.transform.RotateAround(pivot, Vector3.up, angle - last);
            last = angle;
            yield return null;
        }
    }

    // ---------------------------------------------------------------- models

    // The AIRI logo and the UTCN logo side by side on the floor, `height` tall, `gap` apart, centred; one root so
    // they move as one.
    private GameObject LogoPair(float height, float gap)
    {
        var pair = new GameObject("AIRI x UTCN");
        pair.transform.position = center;
        spawned.Add(pair);
        var airi = Hero("Models/AIRI_Intro/AIRI_Intro", height, 0f, 0f, 1.4f);
        var utcn = Hero("Models/UTCN/UTCN", height, 0f, 0f, 1.6f);
        float wa = GesturePlayer.GetBounds(Parts(airi)).size.x, wu = GesturePlayer.GetBounds(Parts(utcn)).size.x;
        float left = -(wa + gap + wu) / 2f;
        Stand(airi, new Vector3(center.x + left + wa / 2f, center.y, center.z));
        Stand(utcn, new Vector3(center.x + left + wa + gap + wu / 2f, center.y, center.z));
        airi.transform.SetParent(pair.transform, true);
        utcn.transform.SetParent(pair.transform, true);
        return pair;
    }

    // A model turned to the viewer like the showcase does (plus `yaw`), at most `height` tall and `width` wide, its
    // base centred on (x, `bottom` above the floor, z).
    private GameObject Hero(string path, float height, float bottom, float yaw = 0f, float? width = null, float? x = null, float? z = null)
    {
        var go = Load(path, Quaternion.Euler(0f, 180f + yaw, 0f));
        var parts = Parts(go);
        if (parts.Length == 0) return go;
        Bounds b = GesturePlayer.GetBounds(parts);
        float factor = Mathf.Min(height / b.size.y, (width ?? 2.4f) / Mathf.Max(b.size.x, b.size.z));
        go.transform.localScale *= factor;
        Stand(go, new Vector3(x ?? center.x, center.y + bottom, z ?? center.z));
        return go;
    }

    private GameObject Load(string path, Quaternion turn)
    {
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null) Debug.LogWarning($"StoryDirector: nothing at Resources/{path}");
        var go = prefab != null ? Instantiate(prefab, center, turn * prefab.transform.rotation) : new GameObject("missing " + path);
        spawned.Add(go);
        return go;
    }

    private GameObject Primitive(PrimitiveType type, string name, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>()); // or floor raycasts would land on it
        go.name = name;
        go.GetComponent<Renderer>().sharedMaterial = material;
        spawned.Add(go);
        return go;
    }

    private void Remove(GameObject go)
    {
        if (go == null) return;
        idle.RemoveAll(i => i.go == go);
        spawned.Remove(go);
        fullScale.Remove(go);
        Destroy(go);
    }

    // Grows out of its own middle with a soft overshoot.
    private IEnumerator GrowFromMiddle(GameObject go, float seconds)
    {
        Vector3 middle = Middle(go);
        yield return Tween(seconds, t =>
        {
            if (go == null) return;
            Scale(go, EaseOutBack(t));
            go.transform.position += middle - Middle(go);
        });
    }

    private IEnumerator ShrinkToMiddle(GameObject go, float seconds)
    {
        if (go == null) yield break;
        Vector3 middle = Middle(go);
        yield return Tween(seconds, t =>
        {
            if (go == null) return;
            Scale(go, 1f - Smooth(t));
            go.transform.position += middle - Middle(go);
        });
        Remove(go);
    }

    // Grows up out of the ground (a tree, a lamp).
    private IEnumerator GrowFromBase(GameObject go, float seconds)
    {
        Vector3 basePoint = new Vector3(go.transform.position.x, Bottom(go).y, go.transform.position.z);
        yield return Tween(seconds, t =>
        {
            if (go == null) return;
            Scale(go, EaseOutBack(t));
            Stand(go, basePoint);
        });
    }

    // One logo turns edge-on and away while the next turns in from the other side.
    private IEnumerator Flip(GameObject from, GameObject to, float seconds)
    {
        Quaternion a = from.transform.rotation, b = to.transform.rotation;
        Vector3 pa = Middle(from), pb = Middle(to);
        yield return Tween(seconds, t =>
        {
            float first = Mathf.Clamp01(t * 2f), second = Mathf.Clamp01(t * 2f - 1f);
            from.transform.rotation = Quaternion.AngleAxis(90f * first * first, Vector3.up) * a;
            Scale(from, 1f - first * first);
            to.transform.rotation = Quaternion.AngleAxis(-90f * (1f - Smooth(second)), Vector3.up) * b;
            Scale(to, EaseOutBack(second));
            from.transform.position += pa - Middle(from);
            to.transform.position += pb - Middle(to);
        });
    }

    // ---------------------------------------------------------------- headlines

    // The headline's letters, in reading order, each with its resting pose.
    private readonly Dictionary<GameObject, List<(Transform letter, Vector3 pos, Quaternion rot, Vector3 scale)>> letters =
        new Dictionary<GameObject, List<(Transform, Vector3, Quaternion, Vector3)>>();

    // Letters rise into place one after another, tipping up a little, settling softly.
    private GameObject Phrase(string name, bool quick = false)
    {
        var go = Load("Story/" + name, Quaternion.Euler(0f, 180f, 0f));
        var parts = Parts(go);
        if (parts.Length == 0) return go;
        Bounds b = GesturePlayer.GetBounds(parts);
        // One type size for every headline (a layout grid, not fill-the-box), smaller for the tracked department names;
        // only a line too wide for the alcove is scaled down.
        float typeSize = name.StartsWith("Dept_") ? 0.22f : 0.38f;
        go.transform.localScale *= Mathf.Min(typeSize, TextWidth / b.size.x, TextHeight / b.size.y);
        b = GesturePlayer.GetBounds(parts);
        go.transform.position += new Vector3(center.x, center.y + TextY, center.z - TextForward) - b.center;

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
            StartCoroutine(Delayed(i * (quick ? 0.015f : 0.035f), Tween(quick ? 0.4f : 0.7f, t =>
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

    // Letters drift up and away one after another, shrinking.
    private IEnumerator PhraseOut(GameObject go, bool quick = false)
    {
        if (go == null || !letters.TryGetValue(go, out var list)) { Remove(go); yield break; }
        for (int i = 0; i < list.Count; i++)
        {
            var (letter, pos, rot, scale) = list[i];
            StartCoroutine(Delayed(i * (quick ? 0.008f : 0.02f), Tween(quick ? 0.3f : 0.5f, t =>
            {
                if (letter == null) return;
                float e = t * t;
                letter.localPosition = pos + Vector3.up * 0.25f * e;
                letter.localRotation = Quaternion.Euler(-25f * e, 0f, 0f) * rot;
                letter.localScale = scale * Mathf.Max(0.001f, 1f - e);
            })));
        }
        yield return new WaitForSeconds(list.Count * (quick ? 0.008f : 0.02f) + (quick ? 0.35f : 0.55f));
        letters.Remove(go);
        Remove(go);
    }

    // ---------------------------------------------------------------- geometry and time

    // Meshes and skinned meshes (animated models are skinned; measuring only plain meshes leaves them unsized).
    private static Renderer[] Parts(GameObject go) => go == null ? new Renderer[0]
        : Array.FindAll(go.GetComponentsInChildren<Renderer>(), r => (r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled);

    private static Vector3 Middle(GameObject go) => Parts(go).Length > 0 ? GesturePlayer.GetBounds(Parts(go)).center : go.transform.position;

    private static Vector3 Top(GameObject go)
    {
        if (Parts(go).Length == 0) return go.transform.position;
        Bounds b = GesturePlayer.GetBounds(Parts(go));
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }

    private static Vector3 Bottom(GameObject go)
    {
        if (Parts(go).Length == 0) return go.transform.position;
        Bounds b = GesturePlayer.GetBounds(Parts(go));
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    private static void Stand(GameObject go, Vector3 point)
    {
        if (go != null) go.transform.position += point - Bottom(go);
    }

    // `factor` of the size it had when first scaled.
    private void Scale(GameObject go, float factor)
    {
        if (go == null) return;
        if (!fullScale.TryGetValue(go, out var full)) fullScale[go] = full = go.transform.localScale;
        go.transform.localScale = full * Mathf.Max(0.001f, factor); // not 0: breaks bounds and matrices
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

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
