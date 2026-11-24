using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LittleWitchCoop
{
    internal sealed class BanEntry
    {
        public string Id;
        public string Name;
    }

    internal sealed class BanStore
    {
        private readonly object gate = new object();
        private readonly string path;
        private Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
        public BanStore(string file = null)
        {
            path = file;
            if (file == null || !File.Exists(file)) return;
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 2 || !ValidId(parts[0])) continue;
                try { entries[parts[0]] = ChatMessage.Clean(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])), 40); }
                catch (FormatException) { }
            }
        }
        private static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            System.Net.IPAddress address;
            ulong steam;
            return id.StartsWith("ip:", StringComparison.Ordinal) && System.Net.IPAddress.TryParse(id.Substring(3), out address)
                || id.StartsWith("steam:", StringComparison.Ordinal) && ulong.TryParse(id.Substring(6), out steam) && steam > 0;
        }
        public bool Contains(string id) { lock (gate) return id != null && entries.ContainsKey(id); }
        public BanEntry[] Entries
        {
            get
            {
                lock (gate)
                {
                    var result = new List<BanEntry>();
                    foreach (var entry in entries) result.Add(new BanEntry { Id = entry.Key, Name = entry.Value });
                    result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                    return result.ToArray();
                }
            }
        }
        public void Add(string id, string name)
        {
            if (!ValidId(id)) throw new ArgumentException("Identificação de jogador inválida.");
            lock (gate)
            {
                var copy = new Dictionary<string, string>(entries, StringComparer.Ordinal);
                copy[id] = ChatMessage.Clean(name, 40);
                Save(copy);
                entries = copy;
            }
        }
        public void Remove(string id)
        {
            lock (gate)
            {
                var copy = new Dictionary<string, string>(entries, StringComparer.Ordinal);
                if (!copy.Remove(id)) return;
                Save(copy);
                entries = copy;
            }
        }
        private void Save(Dictionary<string, string> copy)
        {
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var lines = new List<string>();
            foreach (var entry in copy) lines.Add(entry.Key + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Value)));
            string temporary = path + ".tmp";
            File.WriteAllLines(temporary, lines.ToArray(), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}

