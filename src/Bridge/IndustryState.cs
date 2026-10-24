using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VaspCommunityMods
{
    internal sealed class IndustryEntry
    {
        internal int Level;
        // Exact twenty-fifths: 30/25 at level one, 36/25 at level two.
        internal readonly int[] Remainders = new int[6];
        internal IndustryEntry Copy()
        {
            var copy = new IndustryEntry { Level = Level };
            Array.Copy(Remainders, copy.Remainders, Remainders.Length);
            return copy;
        }
    }

    // Native manager data is optional and ignored by vanilla readers. No Port
    // layout or original save version changes, and no global/per-PC level file.
    public sealed class IndustryState : MonoBehaviour, ISaveableManager, ISaveable
    {
        private readonly Dictionary<string, IndustryEntry> entries = new Dictionary<string, IndustryEntry>(StringComparer.Ordinal);
        private byte[] preservedUnknownData;
        public static IndustryState Instance { get; private set; }
        public string LoadError { get; private set; }
        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        public void OnClearGame() { entries.Clear(); preservedUnknownData = null; LoadError = null; }
        public void OnInitialiseGame() { OnClearGame(); }
        public void OnSaveGame() { SaveManager.instance.SaveManagerReference(this); }
        public void OnLoadGame()
        {
            OnClearGame();
            byte[] data;
            if (!SaveManager.instance.managerReferenceData.TryGetValue(GetType().ToString(), out data)) return;
            if (data == null) return;
            try { Unserialize(data); }
            catch (Exception error)
            {
                entries.Clear(); preservedUnknownData = (byte[])data.Clone();
                LoadError = "Не удалось прочитать уровни производства; покупки отключены. Данные сохранены без изменений.";
                Debug.LogError(LoadError + " " + error);
            }
        }
        internal IndustryEntry Entry(string port) { IndustryEntry value; return entries.TryGetValue(port, out value) ? value : null; }
        internal void SetEntry(string port, IndustryEntry entry) { entries[port] = entry; }
        public byte[] GetSerialized()
        {
            if (preservedUnknownData != null) return (byte[])preservedUnknownData.Clone();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Encoding.ASCII.GetBytes("VASPIND1"));
                writer.Write(entries.Count);
                var keys = new List<string>(entries.Keys); keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    byte[] name = Encoding.UTF8.GetBytes(key); writer.Write(name.Length); writer.Write(name);
                    IndustryEntry entry = entries[key]; writer.Write(entry.Level);
                    foreach (int remainder in entry.Remainders) writer.Write(remainder);
                }
                writer.Flush(); return stream.ToArray();
            }
        }
        public void Unserialize(byte[] data)
        {
            var restored = new Dictionary<string, IndustryEntry>(StringComparer.Ordinal);
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "VASPIND1") throw new FormatException("Unknown industry schema");
                int count = reader.ReadInt32(); if (count < 0 || count > 512) throw new FormatException("Industry entry count");
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadInt32(); if (length < 1 || length > 512 || length > stream.Length - stream.Position) throw new FormatException("Industry port name length");
                    string name = new UTF8Encoding(false, true).GetString(reader.ReadBytes(length));
                    if (restored.ContainsKey(name)) throw new FormatException("Duplicate industry port");
                    var entry = new IndustryEntry { Level = reader.ReadInt32() };
                    if (entry.Level < 0 || entry.Level > 2) throw new FormatException("Industry level");
                    for (int j = 0; j < entry.Remainders.Length; j++)
                    {
                        entry.Remainders[j] = reader.ReadInt32();
                        if (entry.Remainders[j] < 0 || entry.Remainders[j] >= 25) throw new FormatException("Industry fractional remainder");
                    }
                    restored.Add(name, entry);
                }
                if (stream.Position != stream.Length) throw new FormatException("Trailing industry data");
            }
            entries.Clear(); foreach (var item in restored) entries.Add(item.Key, item.Value);
            preservedUnknownData = null; LoadError = null;
        }
    }
}
