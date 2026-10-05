using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;
using UnityEngine.Video;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Minis;
using System;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if SKOTE_VFX_TOOLKIT
using Skote.Vfx;
using VFXManager = Skote.Vfx.VFXManager;
#endif
#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
using Skote.Vfx.Sdf;
#endif
#if SKOTE_DYNAMIC_TEMPORAL_BLUR
using Skote.TemporalBlur;
#endif
#if SKOTE_METAVIDO_EXTENSIONS
using Skote.MetavidoExtensions;
#endif

namespace Skote.Midi
{
    /// <summary>
    /// Runtime manager for live MIDI mapping. Works with MidiMappingWindow for visual editing.
    /// Allows connecting MIDI inputs (Notes and CCs) to targets on-the-fly during play mode AND edit mode.
    /// Targets are pre-defined in Inspector. MIDI inputs are auto-detected as they arrive.
    /// Mappings are serialized and persist across play mode transitions.
    /// </summary>
    [ExecuteAlways]
    public class MidiMappingManager : MonoBehaviour
    {
        public static MidiMappingManager Instance { get; private set; }

        [Header("Pre-defined Targets")]
        [Tooltip("Define the targets you want to be able to control. These appear in the mapping window.")]
        public List<MidiTargetDefinition> targetDefinitions = new List<MidiTargetDefinition>();

        [Header("Envelope Settings (for Note triggers)")]
        public float defaultDecayTime = 0.3f;
        public float defaultBaseValue = 0.2f;
        public float defaultPeakValue = 1.0f;

        [Header("Debug")]
        public bool logMidiInput = false;

        [Header("Saved Mappings")]
        [SerializeField] private List<SerializedMapping> savedMappings = new List<SerializedMapping>();

        // Runtime state - accessible by editor window
        // Devices
        [NonSerialized] public HashSet<string> detectedDevices = new HashSet<string>();
        // Notes - keyed by (deviceName, noteNumber)
        [NonSerialized] public HashSet<MidiInputKey> detectedNotes = new HashSet<MidiInputKey>();
        [NonSerialized] public Dictionary<MidiInputKey, float> noteActivity = new Dictionary<MidiInputKey, float>();
        [NonSerialized] public Dictionary<MidiInputKey, float> noteVelocities = new Dictionary<MidiInputKey, float>();
        // CCs - keyed by (deviceName, ccNumber)
        [NonSerialized] public HashSet<MidiInputKey> detectedCCs = new HashSet<MidiInputKey>();
        [NonSerialized] public Dictionary<MidiInputKey, float> ccActivity = new Dictionary<MidiInputKey, float>();
        [NonSerialized] public Dictionary<MidiInputKey, float> ccValues = new Dictionary<MidiInputKey, float>();

        // Resolved targets from definitions
        [NonSerialized] public List<MidiTarget> availableTargets = new List<MidiTarget>();
        [NonSerialized] public List<LiveMapping> activeMappings = new List<LiveMapping>();

        // Envelope state per mapping
        private Dictionary<LiveMapping, float> _envelopeValues = new Dictionary<LiveMapping, float>();
        // Last CC value per trigger mapping, for rising-edge detection
        private Dictionary<LiveMapping, float> _lastCCValues = new Dictionary<LiveMapping, float>();

        private HashSet<MidiDevice> _subscribedDevices = new HashSet<MidiDevice>();
        private float _lastEditorTime;

        public event Action OnMidiActivity;
        public event Action OnMappingsChanged;
        public event Action OnTargetsDiscovered;

        private void Awake()
        {
            SetupInstance();
        }

        private void SetupInstance()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            Instance = this;
        }

        private void OnEnable()
        {
            SetupInstance();
            SubscribeToMidi();
            DiscoverTargets();
            RestoreMappings();

    #if UNITY_EDITOR
            EditorApplication.update += EditorUpdate;
    #endif
        }

        private void OnDisable()
        {
            UnsubscribeFromMidi();

    #if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
    #endif
        }

        private void SubscribeToMidi()
        {
            InputSystem.onDeviceChange += OnDeviceChange;

            foreach (var device in InputSystem.devices)
            {
                if (device is MidiDevice midiDevice)
                    TrySubscribeToDevice(midiDevice);
            }
        }

        private void UnsubscribeFromMidi()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;

            foreach (var device in _subscribedDevices)
            {
                if (device != null)
                {
                    device.onWillNoteOn -= OnNoteOn;
                    device.onWillControlChange -= OnControlChange;
                }
            }
            _subscribedDevices.Clear();
        }

    #if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (Application.isPlaying) return;
            if (this == null) return;

            // Calculate delta time for editor
            float currentTime = (float)EditorApplication.timeSinceStartup;
            float deltaTime = currentTime - _lastEditorTime;
            _lastEditorTime = currentTime;

            // Clamp to avoid huge jumps
            deltaTime = Mathf.Min(deltaTime, 0.1f);

            ProcessEnvelopeDecay(deltaTime);
        }
    #endif

        private void Update()
        {
            if (!Application.isPlaying) return;
            ProcessEnvelopeDecay(Time.deltaTime);
        }

        private void ProcessEnvelopeDecay(float deltaTime)
        {
            // Process envelope decay for Note mappings with envelope enabled
            // CC mappings are continuous and don't use envelopes
            var mappingsToUpdate = activeMappings
                .Where(m => m.midiType == MidiInputType.Note && m.useEnvelope && m.target != null && !IsTrigger(m.target.targetType))
                .ToList();

            foreach (var mapping in mappingsToUpdate)
            {
                if (!_envelopeValues.ContainsKey(mapping))
                    _envelopeValues[mapping] = mapping.baseValue;

                float current = _envelopeValues[mapping];
                if (current > mapping.baseValue)
                {
                    float decayAmount = (mapping.peakValue - mapping.baseValue) / mapping.decayTime * deltaTime;
                    current = Mathf.Max(mapping.baseValue, current - decayAmount);
                    _envelopeValues[mapping] = current;
                    ApplyValue(mapping.target, current);
                }
            }
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is MidiDevice midiDevice)
            {
                if (change == InputDeviceChange.Added)
                    TrySubscribeToDevice(midiDevice);
                else if (change == InputDeviceChange.Removed)
                    _subscribedDevices.Remove(midiDevice);
            }
        }

        private void TrySubscribeToDevice(MidiDevice device)
        {
            if (_subscribedDevices.Contains(device))
                return;

            device.onWillNoteOn += OnNoteOn;
            device.onWillControlChange += OnControlChange;
            _subscribedDevices.Add(device);

            Debug.Log($"[MidiMapping] Subscribed to: {device.description.product ?? device.name}");
        }

        private float CurrentTime
        {
            get
            {
    #if UNITY_EDITOR
                return Application.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup;
    #else
                return Time.time;
    #endif
            }
        }

        private void OnNoteOn(MidiNoteControl note, float velocity)
        {
            int noteNum = note.noteNumber;
            string deviceName = GetDeviceName(note.device as MidiDevice);
            ProcessNoteOn(deviceName, noteNum, velocity);
        }

        private void ProcessNoteOn(string deviceName, int noteNum, float velocity)
        {
            var key = new MidiInputKey(deviceName, noteNum);

            // Track activity
            detectedDevices.Add(deviceName);
            detectedNotes.Add(key);
            noteActivity[key] = CurrentTime;
            noteVelocities[key] = velocity;

            if (logMidiInput)
                Debug.Log($"[MidiMapping] [{deviceName}] Note {noteNum}, Vel {velocity:F2}");

            OnMidiActivity?.Invoke();

            // Process mappings for this note (matching device and note number)
            foreach (var mapping in activeMappings.Where(m =>
                m.midiType == MidiInputType.Note &&
                m.inputNumber == noteNum &&
                MatchesDevice(m.deviceName, deviceName)))
            {
                if (mapping.target == null) continue;

                if (IsTrigger(mapping.target.targetType))
                {
                    TriggerEvent(mapping.target);
                }
                else if (mapping.useEnvelope)
                {
                    // Trigger envelope
                    float peak = mapping.velocityScalesPeak
                        ? Mathf.Lerp(mapping.baseValue, mapping.peakValue, velocity)
                        : mapping.peakValue;
                    _envelopeValues[mapping] = peak;
                    ApplyValue(mapping.target, peak);
                }
                else
                {
                    // Instant trigger (for events)
                    TriggerEvent(mapping.target);
                }
            }
        }

        private string GetDeviceName(MidiDevice device)
        {
            if (device == null) return "Unknown";
            return device.description.product ?? device.name ?? "Unknown";
        }

        private void OnControlChange(MidiValueControl control, float value)
        {
            int ccNum = control.controlNumber;
            string deviceName = GetDeviceName(control.device as MidiDevice);
            ProcessCC(deviceName, ccNum, value);
        }

        /// <summary>
        /// Simulate a CC event as if it came from a real MIDI device.
        /// Used by MidiSequencer for looped playback.
        /// </summary>
        public void SimulateCC(string deviceName, int ccNumber, float normalizedValue)
        {
            ProcessCC(deviceName, ccNumber, normalizedValue);
        }

        /// <summary>
        /// Simulate a Note On event as if it came from a real MIDI device.
        /// Used by MidiSequencer for looped playback.
        /// </summary>
        public void SimulateNoteOn(string deviceName, int noteNumber, float velocity)
        {
            ProcessNoteOn(deviceName, noteNumber, velocity);
        }

        private void ProcessCC(string deviceName, int ccNum, float value)
        {
            var key = new MidiInputKey(deviceName, ccNum);

            // Track activity
            detectedDevices.Add(deviceName);
            detectedCCs.Add(key);
            ccActivity[key] = CurrentTime;
            ccValues[key] = value;

            if (logMidiInput)
                Debug.Log($"[MidiMapping] [{deviceName}] CC#{ccNum} = {value:F2}");

            OnMidiActivity?.Invoke();

            // Process mappings for this CC (matching device and CC number)
            foreach (var mapping in activeMappings.Where(m =>
                m.midiType == MidiInputType.CC &&
                m.inputNumber == ccNum &&
                MatchesDevice(m.deviceName, deviceName)))
            {
                if (mapping.target == null) continue;

                if (IsTrigger(mapping.target.targetType))
                {
                    // CC buttons send 1 on press and 0 on release: fire on the rising edge only
                    _lastCCValues.TryGetValue(mapping, out float previous);
                    _lastCCValues[mapping] = value;
                    if (value >= 0.5f && previous < 0.5f)
                        TriggerEvent(mapping.target);
                    continue;
                }

                // CCs are continuous - apply value directly (with optional remap)
                float mappedValue = Mathf.Lerp(mapping.baseValue, mapping.peakValue, value);
                ApplyValue(mapping.target, mappedValue);
            }
        }

        /// <summary>
        /// A mapping with an empty device name listens to every device.
        /// </summary>
        public static bool MatchesDevice(string mappingDevice, string deviceName)
        {
            return string.IsNullOrEmpty(mappingDevice) || mappingDevice == deviceName;
        }

        /// <summary>
        /// Trigger targets fire an action instead of receiving a continuous value.
        /// </summary>
        public static bool IsTrigger(MidiTargetType type)
        {
            switch (type)
            {
                case MidiTargetType.VFXEvent:
                case MidiTargetType.VFXManagerAction:
                case MidiTargetType.AnimatorManagerAction:
                case MidiTargetType.AnimatorPause:
                case MidiTargetType.MetavidoAction:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Resolve targets from pre-defined targetDefinitions list
        /// </summary>
        public void DiscoverTargets()
        {
            availableTargets.Clear();

            foreach (var def in targetDefinitions)
            {
                if (def == null || def.targetObject == null) continue;

                var target = def.ResolveTarget();
                if (target != null)
                {
                    availableTargets.Add(target);
                }
                else
                {
                    Debug.LogWarning($"[MidiMapping] Could not resolve target: {def.label} on {def.targetObject.name}");
                }
            }

            OnTargetsDiscovered?.Invoke();
            Debug.Log($"[MidiMapping] Resolved {availableTargets.Count} targets from {targetDefinitions.Count} definitions");
        }

        /// <summary>
        /// Create a new mapping between a MIDI input (Note or CC) and a target
        /// </summary>
        public LiveMapping CreateMapping(MidiInputType midiType, string deviceName, int inputNumber, MidiTarget target)
        {
            // For CC, default to no envelope (continuous control)
            // For Notes, default to envelope (trigger with decay), except for trigger targets
            bool useEnvelope = midiType == MidiInputType.Note && !IsTrigger(target.targetType);

            var mapping = new LiveMapping
            {
                midiType = midiType,
                deviceName = deviceName,
                inputNumber = inputNumber,
                target = target,
                useEnvelope = useEnvelope,
                baseValue = defaultBaseValue,
                peakValue = defaultPeakValue,
                decayTime = defaultDecayTime,
                velocityScalesPeak = false
            };

            activeMappings.Add(mapping);
            _envelopeValues[mapping] = mapping.baseValue;

            // Auto-save
            SaveMappings();
            OnMappingsChanged?.Invoke();

            string typeLabel = midiType == MidiInputType.Note ? "Note" : "CC";
            Debug.Log($"[MidiMapping] Created: [{deviceName}] {typeLabel} {inputNumber} -> {target.displayName}");
            return mapping;
        }

        /// <summary>
        /// Remove a mapping
        /// </summary>
        public void RemoveMapping(LiveMapping mapping)
        {
            activeMappings.Remove(mapping);
            _envelopeValues.Remove(mapping);
            _lastCCValues.Remove(mapping);

            // Auto-save
            SaveMappings();
            OnMappingsChanged?.Invoke();
        }

        /// <summary>
        /// Call this when mapping parameters change (base, peak, decay, etc.)
        /// </summary>
        public void OnMappingParametersChanged()
        {
            SaveMappings();
        }

        /// <summary>
        /// Clear all mappings
        /// </summary>
        public void ClearMappings()
        {
            activeMappings.Clear();
            _envelopeValues.Clear();
            _lastCCValues.Clear();
            savedMappings.Clear();
            MarkDirty();
            OnMappingsChanged?.Invoke();
        }

        /// <summary>
        /// Save current mappings to serialized format (call this to persist)
        /// </summary>
        public void SaveMappings()
        {
            savedMappings.Clear();

            foreach (var mapping in activeMappings)
            {
                if (mapping.target?.component == null) continue;

                var serialized = new SerializedMapping
                {
                    midiType = mapping.midiType,
                    deviceName = mapping.deviceName,
                    inputNumber = mapping.inputNumber,
                    targetPath = GetGameObjectPath(mapping.target.component.gameObject),
                    componentType = mapping.target.component.GetType().AssemblyQualifiedName,
                    targetType = mapping.target.targetType,
                    propertyName = mapping.target.propertyName,
                    useEnvelope = mapping.useEnvelope,
                    baseValue = mapping.baseValue,
                    peakValue = mapping.peakValue,
                    decayTime = mapping.decayTime,
                    velocityScalesPeak = mapping.velocityScalesPeak
                };

                savedMappings.Add(serialized);
            }

            MarkDirty();
            Debug.Log($"[MidiMapping] Saved {savedMappings.Count} mappings");
        }

        /// <summary>
        /// Restore mappings from serialized format
        /// </summary>
        public void RestoreMappings()
        {
            if (savedMappings.Count == 0) return;

            activeMappings.Clear();
            _envelopeValues.Clear();
            _lastCCValues.Clear();

            // Make sure targets are discovered first
            if (availableTargets.Count == 0)
                DiscoverTargets();

            int restored = 0;
            foreach (var saved in savedMappings)
            {
                // Find the target by path and type
                var target = FindTargetByPath(saved);
                if (target == null)
                {
                    string typeLabel = saved.midiType == MidiInputType.Note ? "Note" : "CC";
                    Debug.LogWarning($"[MidiMapping] Could not restore mapping for [{saved.deviceName}] {typeLabel} {saved.inputNumber} -> {saved.targetPath}/{saved.propertyName}");
                    continue;
                }

                var mapping = new LiveMapping
                {
                    midiType = saved.midiType,
                    deviceName = saved.deviceName ?? "",
                    inputNumber = saved.inputNumber,
                    target = target,
                    useEnvelope = saved.useEnvelope,
                    baseValue = saved.baseValue,
                    peakValue = saved.peakValue,
                    decayTime = saved.decayTime,
                    velocityScalesPeak = saved.velocityScalesPeak
                };

                activeMappings.Add(mapping);
                _envelopeValues[mapping] = mapping.baseValue;
                restored++;
            }

            Debug.Log($"[MidiMapping] Restored {restored}/{savedMappings.Count} mappings");
            OnMappingsChanged?.Invoke();
        }

        private string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private MidiTarget FindTargetByPath(SerializedMapping saved)
        {
            // First try to find in available targets
            var match = availableTargets.FirstOrDefault(t =>
                t.targetType == saved.targetType &&
                t.propertyName == saved.propertyName &&
                t.component != null &&
                GetGameObjectPath(t.component.gameObject) == saved.targetPath);

            if (match != null) return match;

            // Try to find the GameObject directly
            var go = GameObject.Find(saved.targetPath);
            if (go == null)
            {
                // Try searching by name only (last part of path)
                string name = saved.targetPath.Split('/').Last();
                go = GameObject.Find(name);
            }

            if (go == null) return null;

            // Get the component
            var componentType = Type.GetType(saved.componentType);
            if (componentType == null) return null;

            var component = go.GetComponent(componentType);
            if (component == null) return null;

            // Create a new target
            return new MidiTarget
            {
                displayName = $"{go.name}/{saved.propertyName}",
                targetType = saved.targetType,
                component = component,
                propertyName = saved.propertyName
            };
        }

        private void MarkDirty()
        {
    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
            }
    #endif
        }

        private void ApplyValue(MidiTarget target, float value)
        {
            if (target?.component == null) return;

            switch (target.targetType)
            {
                case MidiTargetType.VFXFloat:
                    ((VisualEffect)target.component).SetFloat(target.propertyName, value);
                    break;

                case MidiTargetType.AnimatorSpeed:
                    ((AnimatorSpeedController)target.component).SetSpeed(value);
                    break;

#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                case MidiTargetType.AnimatorManagerSpeed:
                    ((AnimatorManager)target.component).SetSpeed(value);
                    break;
#endif

                case MidiTargetType.VideoSpeed:
                    ((VideoPlayer)target.component).playbackSpeed = value;
                    break;

                case MidiTargetType.RotatorSpeed:
                    ((Rotator)target.component).SetSpeed(value);
                    break;

                case MidiTargetType.CameraZ:
                    ((CameraZController)target.component).SetZ(value);
                    break;

                case MidiTargetType.PostProcessing:
                    ApplyPostProcessing((Volume)target.component, target.propertyName, value);
                    break;

#if SKOTE_VFX_TOOLKIT
                case MidiTargetType.VFXManager:
                    ((VFXManager)target.component).SetProperty(target.propertyName, value);
                    break;

                case MidiTargetType.VFXVector2:
                    ((VFXVector2Controller)target.component).SetProperty(target.propertyName, value);
                    break;
#endif

#if SKOTE_DYNAMIC_TEMPORAL_BLUR
                case MidiTargetType.TemporalBlurOpacity:
                    ((DynamicTemporalBlur)target.component).SetDefaultOpacity(value);
                    break;
#endif
            }
        }

        private void TriggerEvent(MidiTarget target)
        {
            if (target?.component == null) return;

            switch (target.targetType)
            {
                case MidiTargetType.VFXEvent:
                    ((VisualEffect)target.component).SendEvent(target.propertyName);
                    break;

                case MidiTargetType.AnimatorPause:
                    ((AnimatorSpeedController)target.component).TogglePause();
                    break;

#if SKOTE_VFX_TOOLKIT
                case MidiTargetType.VFXManagerAction:
                    // A group action (e.g. "KillA", "ReviveB"), otherwise a bool property to toggle
                    var vfxManager = (VFXManager)target.component;
                    if (!string.IsNullOrEmpty(target.propertyName) && !vfxManager.ProcessGroupAction(target.propertyName))
                        vfxManager.ToggleBool(target.propertyName);
                    break;
#endif

#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                case MidiTargetType.AnimatorManagerAction:
                    // An SDF action ("Next<Group>"/"Prev<Group>"), otherwise an animator trigger; empty toggles pause
                    var animManager = (AnimatorManager)target.component;
                    if (string.IsNullOrEmpty(target.propertyName))
                        animManager.TogglePause();
                    else if (!animManager.ProcessSDFAction(target.propertyName))
                        animManager.SendTrigger(target.propertyName);
                    break;
#endif

#if SKOTE_METAVIDO_EXTENSIONS
                case MidiTargetType.MetavidoAction:
                    var metavido = (MetavidoManager)target.component;
                    switch (target.propertyName?.ToLower())
                    {
                        case "play": metavido.Play(); break;
                        case "next": metavido.NextClip(); break;
                        case "previous":
                        case "prev": metavido.PreviousClip(); break;
                        case "random": metavido.RandomClip(); break;
                        case "stop": metavido.Stop(); break;
                        default:
                            Debug.LogWarning($"[MidiMapping] Unknown MetavidoManager action: {target.propertyName}");
                            break;
                    }
                    break;
#endif
            }
        }

        private void ApplyPostProcessing(Volume volume, string property, float value)
        {
            if (volume?.profile == null || string.IsNullOrEmpty(property)) return;

            switch (property.ToLower())
            {
                // ColorAdjustments
                case "postexposure":
                case "exposure":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca1))
                        ca1.postExposure.Override(value);
                    break;
                case "contrast":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca2))
                        ca2.contrast.Override(value);
                    break;
                case "saturation":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca3))
                        ca3.saturation.Override(value);
                    break;
                case "hueshift":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca4))
                        ca4.hueShift.Override(value);
                    break;

                // Bloom
                case "bloomintensity":
                    if (volume.profile.TryGet<Bloom>(out var bloom1))
                        bloom1.intensity.Override(value);
                    break;
                case "bloomthreshold":
                    if (volume.profile.TryGet<Bloom>(out var bloom2))
                        bloom2.threshold.Override(value);
                    break;
                case "bloomscatter":
                    if (volume.profile.TryGet<Bloom>(out var bloom3))
                        bloom3.scatter.Override(value);
                    break;

                // Vignette
                case "vignetteintensity":
                    if (volume.profile.TryGet<Vignette>(out var vig1))
                        vig1.intensity.Override(value);
                    break;
                case "vignettesmoothness":
                    if (volume.profile.TryGet<Vignette>(out var vig2))
                        vig2.smoothness.Override(value);
                    break;

                case "chromaticaberration":
                case "chromatic":
                    if (volume.profile.TryGet<ChromaticAberration>(out var chroma))
                        chroma.intensity.Override(value);
                    break;
                case "motionblur":
                    if (volume.profile.TryGet<MotionBlur>(out var mb))
                        mb.intensity.Override(value);
                    break;
                case "filmgrain":
                case "grain":
                    if (volume.profile.TryGet<FilmGrain>(out var fg))
                        fg.intensity.Override(value);
                    break;

                // WhiteBalance
                case "temperature":
                    if (volume.profile.TryGet<WhiteBalance>(out var wb1))
                        wb1.temperature.Override(value);
                    break;
                case "tint":
                    if (volume.profile.TryGet<WhiteBalance>(out var wb2))
                        wb2.tint.Override(value);
                    break;

                // DepthOfField
                case "focusdistance":
                    if (volume.profile.TryGet<DepthOfField>(out var dof1))
                        dof1.focusDistance.Override(value);
                    break;
                case "aperture":
                    if (volume.profile.TryGet<DepthOfField>(out var dof2))
                        dof2.aperture.Override(value);
                    break;

                case "lensdistortion":
                    if (volume.profile.TryGet<LensDistortion>(out var ld))
                        ld.intensity.Override(value);
                    break;

                default:
                    Debug.LogWarning($"[MidiMapping] Unknown Volume property: {property}");
                    break;
            }
        }
    }

    public enum MidiInputType
    {
        Note,
        CC
    }

    public enum MidiTargetType
    {
        VFXFloat,
        VFXEvent,
        AnimatorSpeed,
        AnimatorManagerSpeed,
        VideoSpeed,
        PostProcessing,
        RotatorSpeed,
        CameraZ,
        VFXManager,
        // Appended to keep serialized values of the types above stable
        VFXVector2,
        TemporalBlurOpacity,
        VFXManagerAction,
        AnimatorManagerAction,
        AnimatorPause,
        MetavidoAction
    }

    /// <summary>
    /// Pre-defined target that appears in the mapping window.
    /// Configure these in the Inspector before runtime.
    /// </summary>
    [Serializable]
    public class MidiTargetDefinition
    {
        [Tooltip("Display name for this target in the mapping window")]
        public string label;

        [Tooltip("The GameObject containing the target component")]
        public GameObject targetObject;

        [Tooltip("Which component type to control")]
        public MidiTargetType targetType;

        [Tooltip("Property name (VFX, Volume, VFXManager, VFXVector2), event name (VFXEvent) or action name (VFXManagerAction, AnimatorManagerAction, MetavidoAction)")]
        public string propertyName;

        /// <summary>
        /// Resolve this definition to a runtime MidiTarget
        /// </summary>
        public MidiTarget ResolveTarget()
        {
            if (targetObject == null) return null;

            Component component = targetType switch
            {
                MidiTargetType.VFXFloat => targetObject.GetComponent<VisualEffect>(),
                MidiTargetType.VFXEvent => targetObject.GetComponent<VisualEffect>(),
                MidiTargetType.AnimatorSpeed => targetObject.GetComponent<AnimatorSpeedController>(),
                MidiTargetType.AnimatorPause => targetObject.GetComponent<AnimatorSpeedController>(),
#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                MidiTargetType.AnimatorManagerSpeed => targetObject.GetComponent<AnimatorManager>(),
                MidiTargetType.AnimatorManagerAction => targetObject.GetComponent<AnimatorManager>(),
#endif
                MidiTargetType.VideoSpeed => targetObject.GetComponent<VideoPlayer>(),
                MidiTargetType.PostProcessing => targetObject.GetComponent<Volume>(),
                MidiTargetType.RotatorSpeed => targetObject.GetComponent<Rotator>(),
                MidiTargetType.CameraZ => targetObject.GetComponent<CameraZController>(),
#if SKOTE_VFX_TOOLKIT
                MidiTargetType.VFXManager => targetObject.GetComponent<VFXManager>(),
                MidiTargetType.VFXManagerAction => targetObject.GetComponent<VFXManager>(),
                MidiTargetType.VFXVector2 => targetObject.GetComponent<VFXVector2Controller>(),
#endif
#if SKOTE_DYNAMIC_TEMPORAL_BLUR
                MidiTargetType.TemporalBlurOpacity => targetObject.GetComponent<DynamicTemporalBlur>(),
#endif
#if SKOTE_METAVIDO_EXTENSIONS
                MidiTargetType.MetavidoAction => targetObject.GetComponent<MetavidoManager>(),
#endif
                _ => null
            };

            if (component == null) return null;

            return new MidiTarget
            {
                displayName = string.IsNullOrEmpty(label) ? $"{targetObject.name}/{propertyName}" : label,
                targetType = targetType,
                component = component,
                propertyName = propertyName
            };
        }
    }

    [Serializable]
    public class MidiTarget
    {
        public string displayName;
        public MidiTargetType targetType;
        public Component component;
        public string propertyName;
    }

    [Serializable]
    public class LiveMapping
    {
        public MidiInputType midiType = MidiInputType.Note;
        public string deviceName; // Which MIDI device this mapping listens to
        public int inputNumber; // Note number or CC number
        public MidiTarget target;
        public bool useEnvelope = true; // For Notes: trigger+decay. For CCs: ignored (continuous)
        public float baseValue = 0.2f;
        public float peakValue = 1.0f;
        public float decayTime = 0.3f;
        public bool velocityScalesPeak = false;
    }

    /// <summary>
    /// Serializable version of a mapping that can persist across play mode and scene saves.
    /// </summary>
    [Serializable]
    public class SerializedMapping
    {
        public MidiInputType midiType = MidiInputType.Note;
        public string deviceName;           // Which MIDI device this mapping listens to
        public int inputNumber;
        public string targetPath;           // GameObject path in hierarchy
        public string componentType;        // Assembly qualified type name
        public MidiTargetType targetType;
        public string propertyName;
        public bool useEnvelope = true;
        public float baseValue = 0.2f;
        public float peakValue = 1.0f;
        public float decayTime = 0.3f;
        public bool velocityScalesPeak = false;
    }

    /// <summary>
    /// Composite key for identifying MIDI inputs by device and number
    /// </summary>
    public struct MidiInputKey : IEquatable<MidiInputKey>
    {
        public string deviceName;
        public int inputNumber;

        public MidiInputKey(string deviceName, int inputNumber)
        {
            this.deviceName = deviceName ?? "Unknown";
            this.inputNumber = inputNumber;
        }

        public bool Equals(MidiInputKey other)
        {
            return deviceName == other.deviceName && inputNumber == other.inputNumber;
        }

        public override bool Equals(object obj)
        {
            return obj is MidiInputKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(deviceName, inputNumber);
        }

        public static bool operator ==(MidiInputKey left, MidiInputKey right) => left.Equals(right);
        public static bool operator !=(MidiInputKey left, MidiInputKey right) => !left.Equals(right);
    }
}
