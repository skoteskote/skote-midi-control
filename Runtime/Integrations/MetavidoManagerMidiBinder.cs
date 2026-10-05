#if SKOTE_METAVIDO_EXTENSIONS
using UnityEngine;
using Skote.MetavidoExtensions;
using UnityEngine.InputSystem;
using Minis;
using System.Collections.Generic;

namespace Skote.Midi
{
    [RequireComponent(typeof(MetavidoManager))]
    public class MetavidoManagerMidiBinder : MonoBehaviour
    {
        [Header("MIDI CC Bindings (Korg nanoKONTROL defaults)")]
        [Tooltip("CC for previous clip")]
        public int previousClipCC = 47;
        [Tooltip("CC for restart current clip")]
        public int restartClipCC = 45;
        [Tooltip("CC for next clip")]
        public int nextClipCC = 48;
        [Tooltip("CC for random clip in current collection")]
        public int randomClipCC = 49;
        [Tooltip("CC for previous collection")]
        public int previousCollectionCC = 46;
        [Tooltip("CC for next collection")]
        public int nextCollectionCC = 44;

        [Header("Direct Collection Select (CC#23-31)")]
        [Tooltip("Collection name to activate when CC#23 is pressed")]
        public string collectionCC23;
        [Tooltip("Collection name to activate when CC#24 is pressed")]
        public string collectionCC24;
        [Tooltip("Collection name to activate when CC#25 is pressed")]
        public string collectionCC25;
        [Tooltip("Collection name to activate when CC#26 is pressed")]
        public string collectionCC26;
        [Tooltip("Collection name to activate when CC#27 is pressed")]
        public string collectionCC27;
        [Tooltip("Collection name to activate when CC#28 is pressed")]
        public string collectionCC28;
        [Tooltip("Collection name to activate when CC#29 is pressed")]
        public string collectionCC29;
        [Tooltip("Collection name to activate when CC#30 is pressed")]
        public string collectionCC30;
        [Tooltip("Collection name to activate when CC#31 is pressed")]
        public string collectionCC31;

        [Header("Debug")]
        public bool logMidiInput = false;

        private MetavidoManager metavidoManager;
        private HashSet<MidiDevice> subscribedDevices = new HashSet<MidiDevice>();
        private Dictionary<int, string> collectionCCLookup = new Dictionary<int, string>();

        private void Awake()
        {
            metavidoManager = GetComponent<MetavidoManager>();
            RebuildCollectionLookup();
        }

        private void RebuildCollectionLookup()
        {
            collectionCCLookup.Clear();
            TryAddLookup(23, collectionCC23);
            TryAddLookup(24, collectionCC24);
            TryAddLookup(25, collectionCC25);
            TryAddLookup(26, collectionCC26);
            TryAddLookup(27, collectionCC27);
            TryAddLookup(28, collectionCC28);
            TryAddLookup(29, collectionCC29);
            TryAddLookup(30, collectionCC30);
            TryAddLookup(31, collectionCC31);
        }

        private void TryAddLookup(int cc, string collectionName)
        {
            if (!string.IsNullOrEmpty(collectionName))
                collectionCCLookup[cc] = collectionName;
        }

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;

            foreach (var device in InputSystem.devices)
            {
                if (device is MidiDevice midiDevice)
                    TrySubscribe(midiDevice);
            }
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;

            foreach (var device in subscribedDevices)
            {
                if (device != null)
                    device.onWillControlChange -= OnControlChange;
            }
            subscribedDevices.Clear();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is MidiDevice midiDevice)
            {
                if (change == InputDeviceChange.Added)
                    TrySubscribe(midiDevice);
                else if (change == InputDeviceChange.Removed)
                    subscribedDevices.Remove(midiDevice);
            }
        }

        private void TrySubscribe(MidiDevice device)
        {
            if (subscribedDevices.Contains(device)) return;

            device.onWillControlChange += OnControlChange;
            subscribedDevices.Add(device);
        }

        private void OnControlChange(MidiValueControl control, float value)
        {
            // Only trigger on button press (value > 0), not release
            if (value <= 0f) return;

            int cc = control.controlNumber;

            if (logMidiInput)
                Debug.Log($"[MetavidoMidiBinder] CC#{cc} = {value:F2}");

            if (cc == previousClipCC)
            {
                metavidoManager.PreviousClip();
            }
            else if (cc == restartClipCC)
            {
                metavidoManager.PlayClipAtIndex(metavidoManager.CurrentClipIndex >= 0 ? metavidoManager.CurrentClipIndex : 0);
            }
            else if (cc == nextClipCC)
            {
                metavidoManager.NextClip();
            }
            else if (cc == randomClipCC)
            {
                metavidoManager.RandomClip();
            }
            else if (cc == previousCollectionCC)
            {
                metavidoManager.PreviousCollection();
            }
            else if (cc == nextCollectionCC)
            {
                metavidoManager.NextCollection();
            }
            else if (collectionCCLookup.TryGetValue(cc, out string collectionName))
            {
                metavidoManager.SetCollectionByName(collectionName);
            }
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            RebuildCollectionLookup();
        }
    #endif
    }
}
#endif
