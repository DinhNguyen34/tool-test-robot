using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModuleTestBms.Models
{
    public class CanSignalDef
    {
        [JsonPropertyName("signalName")]
        public string SignalName { get; set; } = string.Empty;

        [JsonPropertyName("startBit")]
        public int StartBit { get; set; }

        [JsonPropertyName("length")]
        public int Length { get; set; }

        [JsonPropertyName("byteOrder")]
        public string ByteOrder { get; set; } = "Intel";

        [JsonPropertyName("dataType")]
        public string DataType { get; set; } = "Unsigned";

        [JsonPropertyName("factor")]
        public double Factor { get; set; } = 1;

        [JsonPropertyName("offset")]
        public double Offset { get; set; }

        [JsonPropertyName("min")]
        public double Min { get; set; }

        [JsonPropertyName("max")]
        public double Max { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; } = string.Empty;

        [JsonPropertyName("rxNode")]
        public string RxNode { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    public class CanMessageDef
    {
        [JsonPropertyName("messageName")]
        public string MessageName { get; set; } = string.Empty;

        [JsonPropertyName("idHex")]
        public string IdHex { get; set; } = string.Empty;

        [JsonPropertyName("idDec")]
        public uint IdDec { get; set; }

        [JsonPropertyName("dlc")]
        public int Dlc { get; set; }

        [JsonPropertyName("txNode")]
        public string TxNode { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;

        [JsonPropertyName("signals")]
        public List<CanSignalDef> Signals { get; set; } = [];
    }

    public class CanDatabase
    {
        [JsonPropertyName("messages")]
        public List<CanMessageDef> Messages { get; set; } = [];

        private Dictionary<uint, CanMessageDef>? _lookup;

        [JsonIgnore]
        public Dictionary<uint, CanMessageDef> Lookup
        {
            get
            {
                _lookup ??= Messages.ToDictionary(m => m.IdDec, m => m);
                return _lookup;
            }
        }

        public void RebuildLookup() => _lookup = Messages.ToDictionary(m => m.IdDec, m => m);

        /// <summary>
        /// Parse CSV file and build a CanDatabase.
        /// CSV header: Message Name,ID (Hex),ID (Dec),DLC (Bytes),Tx Node,Format,Signal Name,Start Bit,Length (Bits),Byte Order,Data Type,Factor,Offset,Min,Max,Unit,Rx Node,Description &amp; Values
        /// </summary>
        public static CanDatabase ImportFromCsv(string csvPath)
        {
            var db = new CanDatabase();
            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) return db;

            // Skip header row
            var msgDict = new Dictionary<string, CanMessageDef>();

            for (int i = 1; i < lines.Length; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                if (fields.Count < 18) continue;

                string msgName = fields[0].Trim();
                string idHex = fields[1].Trim();
                string idDecStr = fields[2].Trim();
                string dlcStr = fields[3].Trim();
                string txNode = fields[4].Trim();
                string format = fields[5].Trim();
                string sigName = fields[6].Trim();
                string startBitStr = fields[7].Trim();
                string lengthStr = fields[8].Trim();
                string byteOrder = fields[9].Trim();
                string dataType = fields[10].Trim();
                string factorStr = fields[11].Trim();
                string offsetStr = fields[12].Trim();
                string minStr = fields[13].Trim();
                string maxStr = fields[14].Trim();
                string unit = fields[15].Trim().Trim('"');
                string rxNode = fields[16].Trim();
                string description = fields[17].Trim().Trim('"');

                // Build or reuse message
                string msgKey = $"{msgName}_{idHex}";
                if (!msgDict.TryGetValue(msgKey, out var msgDef))
                {
                    msgDef = new CanMessageDef
                    {
                        MessageName = msgName,
                        IdHex = idHex,
                        IdDec = ParseUint(idDecStr),
                        Dlc = int.TryParse(dlcStr, out int dlc) ? dlc : 0,
                        TxNode = txNode,
                        Format = format
                    };
                    msgDict[msgKey] = msgDef;
                    db.Messages.Add(msgDef);
                }

                // Add signal
                if (!string.IsNullOrEmpty(sigName))
                {
                    msgDef.Signals.Add(new CanSignalDef
                    {
                        SignalName = sigName,
                        StartBit = int.TryParse(startBitStr, out int sb) ? sb : 0,
                        Length = int.TryParse(lengthStr, out int len) ? len : 0,
                        ByteOrder = byteOrder,
                        DataType = dataType,
                        Factor = double.TryParse(factorStr, CultureInfo.InvariantCulture, out double f) ? f : 1,
                        Offset = double.TryParse(offsetStr, CultureInfo.InvariantCulture, out double o) ? o : 0,
                        Min = double.TryParse(minStr, CultureInfo.InvariantCulture, out double mn) ? mn : 0,
                        Max = double.TryParse(maxStr, CultureInfo.InvariantCulture, out double mx) ? mx : 0,
                        Unit = unit,
                        RxNode = rxNode,
                        Description = description
                    });
                }
            }

            db.RebuildLookup();
            return db;
        }

        public string SaveToJson(string? outputPath = null)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            outputPath ??= Path.Combine(folder, "BmsCanDatabase.json");
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outputPath, json);
            return outputPath;
        }

        public static CanDatabase? LoadFromJson(string? path = null)
        {
            path ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "BmsCanDatabase.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var db = JsonSerializer.Deserialize<CanDatabase>(json);
            db?.RebuildLookup();
            return db;
        }

        #region CSV Parsing Helpers

        private static uint ParseUint(string s)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : 0;
            return uint.TryParse(s, out uint d) ? d : 0;
        }

        /// <summary>
        /// Parse a single CSV line respecting quoted fields that may contain commas.
        /// </summary>
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuote = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuote = !inQuote;
                    }
                }
                else if (c == ',' && !inQuote)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }

        #endregion
    }
}
