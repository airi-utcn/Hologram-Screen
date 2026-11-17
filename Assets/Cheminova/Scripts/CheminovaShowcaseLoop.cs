using System;
using UnityEngine;

namespace Cheminova
{
    // Fit the unchanged, full-size pair first; animate its children after GesturePlayer.
    [DefaultExecutionOrder(1000)]
    public sealed class CheminovaShowcaseLoop : MonoBehaviour
    {
        private CheminovaSettings settings;
        private GesturePlayer player;
        private CheminovaShoeMaterial shoeMaterial;
        private Transform shoe, building, shoePivot, buildingPivot;
        private CheminovaCycleClock clock;
        private bool ready, restart = true;
        private CheminovaInteractionWindow interaction;
        private Action restorePlayerPose;
        private CheminovaTransformState shoeBeforeInteraction, buildingBeforeInteraction;
        private bool subscribed;
        internal CheminovaTransformState LastRootPose { get; private set; }
        internal CheminovaTransformState LastShoePose { get; private set; }
        internal CheminovaTransformState LastBuildingPose { get; private set; }

        internal float VisibleModelScale
        {
            get
            {
                if (!ready) return 1f;
                bool zoomIn = clock.Phase == CheminovaCyclePhase.ShoeZoomIn || clock.Phase == CheminovaCyclePhase.BuildingZoomIn;
                bool zoomOut = clock.Phase == CheminovaCyclePhase.ShoeZoomOut || clock.Phase == CheminovaCyclePhase.BuildingZoomOut;
                return ZoomScale(clock.Progress, zoomIn, zoomOut);
            }
        }

        public void Init(Transform shoeModel, Transform buildingModel, RevealSequence legacy, CheminovaSettings profile)
        {
            if (settings != null) return;
            if (shoeModel == null || buildingModel == null || legacy == null || profile == null) return;
            shoe = shoeModel;
            shoeMaterial = shoe.GetComponent<CheminovaShoeMaterial>();
            building = buildingModel;
            settings = profile;
            player = GetComponent<GesturePlayer>();
            if (isActiveAndEnabled) Subscribe();
            // The legacy component created the hierarchy; its animation is replaced only at runtime.
            legacy.enabled = false;
            if (player != null) player.Spin = false;
        }

        private void Start()
        {
            if (settings == null) { enabled = false; return; }
            shoePivot = CenteredPivot(shoe, "Cheminova shoe pivot");
            buildingPivot = CenteredPivot(building, "Cheminova building pivot");
            double buildingSpeed = settings.buildingTurnSpeed > 0f ? settings.buildingTurnSpeed : settings.turnSpeed;
            double rotationSeconds = Math.Max(1, settings.turnsPerModel) * 360.0 / Math.Max(0.01, buildingSpeed);
            double shoeRotationSeconds = Math.Max(1, settings.shoeTurns) * 360.0 / Math.Max(0.01, settings.turnSpeed);
            clock = new CheminovaCycleClock(shoeRotationSeconds, rotationSeconds,
                Math.Max(0.01, settings.zoomOutSeconds), Math.Max(0.01, settings.zoomInSeconds));
            interaction = new CheminovaInteractionWindow(Math.Max(0.01, settings.interactionResumeSeconds));
            ready = true;
            ResetCycle();
        }

        private Transform CenteredPivot(Transform model, string name)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(transform, false);
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            pivot.position = renderers.Length > 0 ? GesturePlayer.GetBounds(renderers).center : model.position;
            model.SetParent(pivot, true);
            return pivot;
        }

        private void OnEnable()
        {
            restart = true;
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed || player == null) return;
            player.Interacting += OnInteraction;
            player.ControlsUpdated += OnControlsUpdated;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (player != null && subscribed)
            {
                player.Interacting -= OnInteraction;
                player.ControlsUpdated -= OnControlsUpdated;
            }
            subscribed = false;
        }

        private void OnInteraction()
        {
            if (!ready || !isActiveAndEnabled) return;
            if (restart) ResetCycle();
            if (!interaction.Record(Time.unscaledTimeAsDouble)) return;
            restorePlayerPose = player.CapturePoseRestorer();
            shoeBeforeInteraction = new CheminovaTransformState(shoePivot);
            buildingBeforeInteraction = new CheminovaTransformState(buildingPivot);
        }

        private void OnControlsUpdated()
        {
            if (!ready) return;
            // Runs before GesturePlayer's automatic spin, keeping the paused object still.
            player.Spin = false;
            if (!interaction.IsActive) return;
            LastRootPose = new CheminovaTransformState(transform);
            LastShoePose = new CheminovaTransformState(shoe);
            LastBuildingPose = new CheminovaTransformState(building);
        }

        private void RestoreInteraction()
        {
            if (restorePlayerPose != null)
            {
                restorePlayerPose();
                shoeBeforeInteraction.Restore(shoePivot);
                buildingBeforeInteraction.Restore(buildingPivot);
                restorePlayerPose = null;
            }
            interaction?.Clear();
        }

        private void Update()
        {
            if (!ready) return;
            if (player != null) player.Spin = false;
            if (restart) { ResetCycle(); return; }
            if (interaction.IsActive)
            {
                if (interaction.HasExpired(Time.unscaledTimeAsDouble))
                {
                    RestoreInteraction();
                    ApplyPose();
                }
                return;
            }
            clock.Advance(Time.deltaTime);
            ApplyPose();
        }

        private void ResetCycle()
        {
            restart = false;
            clock.Reset();
            if (player != null) player.Spin = false;
            ApplyPose();
        }

        private void ApplyPose()
        {
            bool showShoe = clock.Phase == CheminovaCyclePhase.ShoeTurns || clock.Phase == CheminovaCyclePhase.ShoeZoomOut
                || clock.Phase == CheminovaCyclePhase.ShoeZoomIn;
            bool zoomOut = clock.Phase == CheminovaCyclePhase.ShoeZoomOut || clock.Phase == CheminovaCyclePhase.BuildingZoomOut;
            bool zoomIn = clock.Phase == CheminovaCyclePhase.ShoeZoomIn || clock.Phase == CheminovaCyclePhase.BuildingZoomIn;
            int turns = showShoe ? settings.shoeTurns : settings.turnsPerModel;
            float angle = Math.Max(1, turns) * 360f * (zoomOut ? 1f : zoomIn ? 0f : clock.Progress);
            float scale = ZoomScale(clock.Progress, zoomIn, zoomOut);
            shoePivot.localRotation = Quaternion.AngleAxis(showShoe ? angle : 0f, Vector3.up);
            buildingPivot.localRotation = Quaternion.AngleAxis(showShoe ? 0f : angle, Vector3.up);
            shoePivot.localScale = Vector3.one * Mathf.Max(0.01f, settings.shoeScaleMultiplier) * (showShoe ? scale : 1f);
            buildingPivot.localScale = Vector3.one * (showShoe ? 1f : scale);
            shoePivot.gameObject.SetActive(showShoe);
            buildingPivot.gameObject.SetActive(!showShoe);
            if (shoeMaterial != null) shoeMaterial.SetAlternateAlbedo(clock.UseAlternateShoeAlbedo);
        }

        internal static float ZoomScale(float progress, bool zoomIn, bool zoomOut)
        {
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            return Mathf.Max(0.0001f, zoomIn ? eased : zoomOut ? 1f - eased : 1f);
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestoreInteraction();
            restart = true;
            if (shoeMaterial != null) shoeMaterial.SetAlternateAlbedo(false);
            if (!ready) return;
            // GesturePlayer re-grounds the pair on re-enable, so restore its original bounds first.
            foreach (var pivot in new[] { shoePivot, buildingPivot })
            {
                if (pivot == null) continue;
                pivot.localRotation = Quaternion.identity;
                pivot.localScale = Vector3.one;
                pivot.gameObject.SetActive(true);
            }
            if (player != null) player.Spin = false;
        }

        private void OnDestroy() => Unsubscribe();
    }

    internal enum CheminovaCyclePhase { ShoeTurns, ShoeZoomOut, BuildingZoomIn, BuildingTurns, BuildingZoomOut, ShoeZoomIn }

    // A time-driven cycle: transitions do not depend on frame rate or animation update order.
    internal sealed class CheminovaCycleClock
    {
        private readonly double shoeRotationSeconds, buildingRotationSeconds, zoomOutSeconds, zoomInSeconds;
        private double elapsed;
        internal CheminovaCyclePhase Phase { get; private set; }
        internal float Progress => (float)(elapsed / Duration);
        internal bool UseAlternateShoeAlbedo => Phase == CheminovaCyclePhase.ShoeZoomOut
            || (Phase == CheminovaCyclePhase.ShoeTurns && elapsed >= shoeRotationSeconds * 0.5);
        private double Duration => Phase == CheminovaCyclePhase.ShoeTurns ? shoeRotationSeconds
            : Phase == CheminovaCyclePhase.BuildingTurns ? buildingRotationSeconds
            : Phase == CheminovaCyclePhase.ShoeZoomIn || Phase == CheminovaCyclePhase.BuildingZoomIn
            ? zoomInSeconds : zoomOutSeconds;

        internal CheminovaCycleClock(double shoeRotationSeconds, double buildingRotationSeconds, double zoomOutSeconds, double zoomInSeconds)
        {
            if (double.IsNaN(shoeRotationSeconds) || double.IsInfinity(shoeRotationSeconds) || shoeRotationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(shoeRotationSeconds));
            if (double.IsNaN(buildingRotationSeconds) || double.IsInfinity(buildingRotationSeconds) || buildingRotationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(buildingRotationSeconds));
            if (double.IsNaN(zoomOutSeconds) || double.IsInfinity(zoomOutSeconds) || zoomOutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(zoomOutSeconds));
            if (double.IsNaN(zoomInSeconds) || double.IsInfinity(zoomInSeconds) || zoomInSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(zoomInSeconds));
            this.shoeRotationSeconds = shoeRotationSeconds;
            this.buildingRotationSeconds = buildingRotationSeconds;
            this.zoomOutSeconds = zoomOutSeconds;
            this.zoomInSeconds = zoomInSeconds;
        }

        internal void Reset() { Phase = CheminovaCyclePhase.ShoeTurns; elapsed = 0; }

        internal void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            seconds %= shoeRotationSeconds + buildingRotationSeconds + 2 * (zoomOutSeconds + zoomInSeconds);
            while (seconds > 0)
            {
                double remaining = Duration - elapsed;
                if (seconds < remaining) { elapsed += seconds; break; }
                seconds -= remaining;
                elapsed = 0;
                Phase = (CheminovaCyclePhase)(((int)Phase + 1) % 6);
            }
        }
    }
}
