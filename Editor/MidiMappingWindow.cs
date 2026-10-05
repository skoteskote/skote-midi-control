using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Skote.Midi.Editor
{
    /// <summary>
    /// Editor window for live MIDI mapping. Works in BOTH Edit and Play mode.
    /// Left panel: Detected MIDI notes (auto-populated)
    /// Center panel: Active mappings with envelope controls
    /// Right panel: Available targets
    /// Click note + target to connect. Mappings auto-save to scene.
    /// </summary>
    public class MidiMappingWindow : EditorWindow
    {
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private Vector2 _mappingsScroll;

        // Selection state - either a Note or CC can be selected, now includes device
        private MidiInputType? _selectedMidiType = null;
        private string _selectedDeviceName = null;
        private int? _selectedInputNumber = null;
        private MidiTarget _selectedTarget = null;

        // Device foldout state
        private Dictionary<string, bool> _deviceFoldouts = new Dictionary<string, bool>();

        private string _targetFilter = "";
        private bool _showOnlyActive = false;
        private bool _showNotes = true;
        private bool _showCCs = true;

        // Styles
        private GUIStyle _activeNoteStyle;
        private GUIStyle _inactiveNoteStyle;
        private GUIStyle _selectedStyle;
        private GUIStyle _mappingStyle;
        private GUIStyle _headerStyle;
        private bool _stylesInitialized = false;

        [MenuItem("Window/MIDI Mapping")]
        public static void ShowWindow()
        {
            var window = GetWindow<MidiMappingWindow>("MIDI Mapping");
            window.minSize = new Vector2(800, 500);
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _activeNoteStyle = new GUIStyle(EditorStyles.helpBox)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 5, 5)
            };

            _inactiveNoteStyle = new GUIStyle(EditorStyles.helpBox)
            {
                normal = { textColor = Color.gray },
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 5, 5)
            };

            _selectedStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 5, 5)
            };

            _mappingStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 4, 4)
            };

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            var manager = MidiMappingManager.Instance;

            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                if (manager == null)
                {
                    if (GUILayout.Button("Create MidiMappingManager", GUILayout.Width(200)))
                    {
                        var go = new GameObject("MidiMappingManager");
                        Undo.RegisterCreatedObjectUndo(go, "Create MidiMappingManager");
                        go.AddComponent<MidiMappingManager>();
                    }
                }
                else
                {
                    if (GUILayout.Button("Refresh Targets", EditorStyles.toolbarButton, GUILayout.Width(100)))
                    {
                        manager.DiscoverTargets();
                    }

                    if (GUILayout.Button("Clear Mappings", EditorStyles.toolbarButton, GUILayout.Width(100)))
                    {
                        if (EditorUtility.DisplayDialog("Clear Mappings", "Remove all MIDI mappings?", "Clear", "Cancel"))
                        {
                            manager.ClearMappings();
                        }
                    }

                    GUILayout.FlexibleSpace();

                    // Mode indicator
                    string modeLabel = Application.isPlaying ? "PLAY" : "EDIT";
                    Color modeColor = Application.isPlaying ? Color.green : Color.yellow;
                    Color oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = modeColor;
                    GUILayout.Label(modeLabel, EditorStyles.toolbarButton, GUILayout.Width(40));
                    GUI.backgroundColor = oldBg;

                    _showNotes = GUILayout.Toggle(_showNotes, "Notes", EditorStyles.toolbarButton, GUILayout.Width(50));
                    _showCCs = GUILayout.Toggle(_showCCs, "CCs", EditorStyles.toolbarButton, GUILayout.Width(40));
                    _showOnlyActive = GUILayout.Toggle(_showOnlyActive, "Active", EditorStyles.toolbarButton, GUILayout.Width(50));

                    manager.logMidiInput = GUILayout.Toggle(manager.logMidiInput, "Log", EditorStyles.toolbarButton, GUILayout.Width(35));
                }
            }
            EditorGUILayout.EndHorizontal();

            if (manager == null)
            {
                EditorGUILayout.HelpBox("Add MidiMappingManager to scene to start mapping MIDI.", MessageType.Info);
                return;
            }

            // Main content - three columns
            EditorGUILayout.BeginHorizontal();
            {
                // Left panel - MIDI Notes
                DrawNotesPanel(manager);

                // Divider
                GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));

                // Center panel - Active Mappings
                DrawMappingsPanel(manager);

                // Divider
                GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));

                // Right panel - Targets
                DrawTargetsPanel(manager);
            }
            EditorGUILayout.EndHorizontal();

            // Instructions
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                GUILayout.Label("Send MIDI to populate inputs. Click input + target to connect. Pre-define targets in Inspector.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNotesPanel(MidiMappingManager manager)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            {
                GUILayout.Label("MIDI Inputs", _headerStyle);
                GUILayout.Space(5);

                _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
                {
                    float currentTime = Application.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup;
                    bool hasAnyInputs = false;

                    // Group by device
                    var devices = manager.detectedDevices.OrderBy(d => d).ToList();

                    foreach (var deviceName in devices)
                    {
                        // Get notes and CCs for this device
                        var deviceNotes = _showNotes
                            ? manager.detectedNotes.Where(k => k.deviceName == deviceName).OrderBy(k => k.inputNumber).ToList()
                            : new List<MidiInputKey>();
                        var deviceCCs = _showCCs
                            ? manager.detectedCCs.Where(k => k.deviceName == deviceName).OrderBy(k => k.inputNumber).ToList()
                            : new List<MidiInputKey>();

                        if (deviceNotes.Count == 0 && deviceCCs.Count == 0) continue;

                        hasAnyInputs = true;

                        // Device foldout
                        if (!_deviceFoldouts.ContainsKey(deviceName))
                            _deviceFoldouts[deviceName] = true;

                        _deviceFoldouts[deviceName] = EditorGUILayout.Foldout(_deviceFoldouts[deviceName], deviceName, true, EditorStyles.foldoutHeader);

                        if (_deviceFoldouts[deviceName])
                        {
                            EditorGUI.indentLevel++;

                            // Draw Notes for this device
                            if (deviceNotes.Count > 0)
                            {
                                EditorGUILayout.LabelField("Notes", EditorStyles.miniLabel);
                                foreach (var key in deviceNotes)
                                {
                                    DrawMidiInputButton(manager, MidiInputType.Note, key.deviceName, key.inputNumber, currentTime,
                                        manager.noteActivity, manager.noteVelocities);
                                }
                                GUILayout.Space(4);
                            }

                            // Draw CCs for this device
                            if (deviceCCs.Count > 0)
                            {
                                EditorGUILayout.LabelField("CCs", EditorStyles.miniLabel);
                                foreach (var key in deviceCCs)
                                {
                                    DrawMidiInputButton(manager, MidiInputType.CC, key.deviceName, key.inputNumber, currentTime,
                                        manager.ccActivity, manager.ccValues);
                                }
                            }

                            EditorGUI.indentLevel--;
                            GUILayout.Space(8);
                        }
                    }

                    if (!hasAnyInputs)
                    {
                        EditorGUILayout.HelpBox("Send MIDI to populate...\n(Notes, CCs, etc.)", MessageType.None);
                    }
                }
                EditorGUILayout.EndScrollView();

                // Selection preview
                if (_selectedInputNumber.HasValue && _selectedMidiType.HasValue && _selectedDeviceName != null)
                {
                    GUILayout.Space(5);
                    string typeLabel = _selectedMidiType.Value == MidiInputType.Note ? "Note" : "CC";
                    EditorGUILayout.HelpBox($"Selected: {typeLabel} {_selectedInputNumber.Value}\n({_selectedDeviceName})\nClick a target to connect", MessageType.Info);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawMidiInputButton(MidiMappingManager manager, MidiInputType midiType, string deviceName, int inputNumber,
            float currentTime, Dictionary<MidiInputKey, float> activityDict, Dictionary<MidiInputKey, float> valueDict)
        {
            var key = new MidiInputKey(deviceName, inputNumber);
            float lastActivity = activityDict.ContainsKey(key) ? activityDict[key] : 0;
            float timeSince = currentTime - lastActivity;
            bool isActive = timeSince < 0.3f;

            if (_showOnlyActive && !isActive) return;

            Color bgColor = GUI.backgroundColor;

            // Active highlight
            if (isActive)
            {
                float value = valueDict.ContainsKey(key) ? valueDict[key] : 0.5f;
                GUI.backgroundColor = Color.Lerp(Color.green, Color.yellow, value);
            }

            // Selection highlight
            bool isSelected = _selectedMidiType == midiType && _selectedDeviceName == deviceName && _selectedInputNumber == inputNumber;
            if (isSelected)
                GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);

            // Count mappings for this input (matching device + type + number)
            int mappingCount = manager.activeMappings.Count(m =>
                m.midiType == midiType && MidiMappingManager.MatchesDevice(m.deviceName, deviceName) && m.inputNumber == inputNumber);
            string typePrefix = midiType == MidiInputType.Note ? "N" : "CC";
            string label = mappingCount > 0 ? $"{typePrefix}{inputNumber} ({mappingCount})" : $"{typePrefix}{inputNumber}";

            // Show current value for CCs
            if (midiType == MidiInputType.CC && valueDict.ContainsKey(key))
            {
                label += $" [{valueDict[key]:F2}]";
            }

            var style = isActive ? _activeNoteStyle : _inactiveNoteStyle;
            if (isSelected) style = _selectedStyle;

            if (GUILayout.Button(label, style, GUILayout.Height(24)))
            {
                if (isSelected)
                {
                    // Deselect
                    _selectedMidiType = null;
                    _selectedDeviceName = null;
                    _selectedInputNumber = null;
                }
                else
                {
                    // Select
                    _selectedMidiType = midiType;
                    _selectedDeviceName = deviceName;
                    _selectedInputNumber = inputNumber;

                    // If we have a target selected, create mapping
                    TryCreateMapping(manager);
                }
            }

            GUI.backgroundColor = bgColor;
        }

        private void TryCreateMapping(MidiMappingManager manager)
        {
            if (_selectedInputNumber.HasValue && _selectedMidiType.HasValue && _selectedDeviceName != null && _selectedTarget != null)
            {
                manager.CreateMapping(_selectedMidiType.Value, _selectedDeviceName, _selectedInputNumber.Value, _selectedTarget);
                _selectedMidiType = null;
                _selectedDeviceName = null;
                _selectedInputNumber = null;
                _selectedTarget = null;
            }
        }

        private void DrawMappingsPanel(MidiMappingManager manager)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(300));
            {
                GUILayout.Label("Active Mappings", _headerStyle);
                GUILayout.Space(5);

                _mappingsScroll = EditorGUILayout.BeginScrollView(_mappingsScroll);
                {
                    if (manager.activeMappings.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No mappings yet.\nSelect a MIDI input and a target to connect.", MessageType.None);
                    }

                    LiveMapping toRemove = null;

                    foreach (var mapping in manager.activeMappings)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        {
                            EditorGUILayout.BeginHorizontal();
                            {
                                // MIDI input indicator with activity
                                var key = new MidiInputKey(mapping.deviceName, mapping.inputNumber);
                                var activityDict = mapping.midiType == MidiInputType.Note ? manager.noteActivity : manager.ccActivity;
                                float lastActivity = activityDict.ContainsKey(key) ? activityDict[key] : 0;
                                float currentTime = Application.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup;
                                bool isActive = currentTime - lastActivity < 0.3f;

                                Color c = GUI.backgroundColor;
                                if (isActive) GUI.backgroundColor = Color.green;

                                string typePrefix = mapping.midiType == MidiInputType.Note ? "N" : "CC";
                                GUILayout.Label($"[{typePrefix}{mapping.inputNumber}]", EditorStyles.miniButton, GUILayout.Width(45));
                                GUI.backgroundColor = c;

                                GUILayout.Label("->", GUILayout.Width(20));
                                GUILayout.Label(mapping.target?.displayName ?? "???", EditorStyles.boldLabel);

                                GUILayout.FlexibleSpace();

                                if (GUILayout.Button("X", GUILayout.Width(20)))
                                {
                                    toRemove = mapping;
                                }
                            }
                            EditorGUILayout.EndHorizontal();

                            // Show device name
                            EditorGUILayout.LabelField(string.IsNullOrEmpty(mapping.deviceName) ? "Device: Any" : $"Device: {mapping.deviceName}", EditorStyles.miniLabel);

                            // Settings row - different for Notes vs CCs
                            EditorGUI.BeginChangeCheck();

                            if (mapping.target != null && MidiMappingManager.IsTrigger(mapping.target.targetType))
                            {
                                // Trigger targets fire on note-on / CC press; no range or envelope
                                string action = string.IsNullOrEmpty(mapping.target.propertyName) ? mapping.target.targetType.ToString() : mapping.target.propertyName;
                                EditorGUILayout.LabelField($"Trigger: {action}", EditorStyles.miniLabel);
                            }
                            else if (mapping.midiType == MidiInputType.Note)
                            {
                                // Note: Envelope settings (trigger + decay)
                                EditorGUILayout.BeginHorizontal();
                                {
                                    mapping.useEnvelope = EditorGUILayout.Toggle(mapping.useEnvelope, GUILayout.Width(15));
                                    GUILayout.Label("Env", GUILayout.Width(25));

                                    GUI.enabled = mapping.useEnvelope;
                                    GUILayout.Label("Base", GUILayout.Width(30));
                                    mapping.baseValue = EditorGUILayout.FloatField(mapping.baseValue, GUILayout.Width(40));

                                    GUILayout.Label("Peak", GUILayout.Width(30));
                                    mapping.peakValue = EditorGUILayout.FloatField(mapping.peakValue, GUILayout.Width(40));

                                    GUILayout.Label("Decay", GUILayout.Width(35));
                                    mapping.decayTime = EditorGUILayout.FloatField(mapping.decayTime, GUILayout.Width(40));
                                    GUI.enabled = true;
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                            else
                            {
                                // CC: Continuous remap (min/max output range)
                                EditorGUILayout.BeginHorizontal();
                                {
                                    GUILayout.Label("Range:", GUILayout.Width(45));
                                    GUILayout.Label("Min", GUILayout.Width(25));
                                    mapping.baseValue = EditorGUILayout.FloatField(mapping.baseValue, GUILayout.Width(50));

                                    GUILayout.Label("Max", GUILayout.Width(25));
                                    mapping.peakValue = EditorGUILayout.FloatField(mapping.peakValue, GUILayout.Width(50));

                                    // Show current value
                                    var ccKey = new MidiInputKey(mapping.deviceName, mapping.inputNumber);
                                    if (manager.ccValues.ContainsKey(ccKey))
                                    {
                                        float ccVal = manager.ccValues[ccKey];
                                        float mapped = Mathf.Lerp(mapping.baseValue, mapping.peakValue, ccVal);
                                        GUILayout.Label($"= {mapped:F2}", GUILayout.Width(50));
                                    }
                                }
                                EditorGUILayout.EndHorizontal();
                            }

                            if (EditorGUI.EndChangeCheck())
                            {
                                manager.OnMappingParametersChanged();
                            }
                        }
                        EditorGUILayout.EndVertical();
                    }

                    if (toRemove != null)
                        manager.RemoveMapping(toRemove);
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTargetsPanel(MidiMappingManager manager)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Targets", _headerStyle, GUILayout.ExpandWidth(true));
                }
                EditorGUILayout.EndHorizontal();

                // Filter
                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Filter:", GUILayout.Width(40));
                    _targetFilter = EditorGUILayout.TextField(_targetFilter);
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                        _targetFilter = "";
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(5);

                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                {
                    var targets = manager.availableTargets;
                    if (!string.IsNullOrEmpty(_targetFilter))
                    {
                        targets = targets.Where(t => t.displayName.ToLower().Contains(_targetFilter.ToLower())).ToList();
                    }

                    // Group by object name
                    var grouped = targets.GroupBy(t => t.displayName.Split('/')[0]);

                    foreach (var group in grouped)
                    {
                        EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);

                        foreach (var target in group)
                        {
                            bool isSelected = _selectedTarget == target;
                            int targetMappingCount = manager.activeMappings.Count(m => m.target == target);
                            bool isMapped = targetMappingCount > 0;

                            Color bgColor = GUI.backgroundColor;
                            if (isSelected)
                                GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
                            else if (isMapped)
                                GUI.backgroundColor = new Color(0.4f, 0.7f, 0.4f);

                            string propName = target.displayName.Contains("/")
                                ? target.displayName.Split('/').Last()
                                : target.displayName;

                            string label = isMapped ? $"  {propName} ({targetMappingCount})" : $"  {propName}";

                            if (GUILayout.Button(label, isSelected ? _selectedStyle : _mappingStyle, GUILayout.Height(22)))
                            {
                                _selectedTarget = isSelected ? null : target;

                                // If we have a MIDI input selected, create mapping
                                TryCreateMapping(manager);
                            }

                            GUI.backgroundColor = bgColor;
                        }

                        GUILayout.Space(3);
                    }

                    if (targets.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No targets defined.\nAdd targets in MidiMappingManager Inspector,\nthen click 'Refresh Targets'.", MessageType.Info);
                    }
                }
                EditorGUILayout.EndScrollView();

                // Target preview
                if (_selectedTarget != null)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.HelpBox($"Selected: {_selectedTarget.displayName}\nClick a note to connect", MessageType.Info);
                }
            }
            EditorGUILayout.EndVertical();
        }
    }
}
