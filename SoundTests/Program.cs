using Faraz.OutlookAlert;
using System.Reflection;
using System.Security.Cryptography;

int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
    passed++;
}

Check(AlertSoundCatalog.Choices.Count == 6, "Five sound choices and Silent are available");
Check(AlertSoundCatalog.Choices.Select(choice => choice.Id).Distinct().Count() == 6, "Choice IDs are unique");
Check(AlertSoundCatalog.Normalize(null) == "chime", "Missing old settings select the default chime");
Check(AlertSoundCatalog.Normalize("") == "chime", "Empty settings select the default chime");
Check(AlertSoundCatalog.Normalize("unavailable-old-sound") == "chime", "Unknown settings select the default chime");
Check(AlertSoundCatalog.Normalize("DOUBLE-BEEP") == "double-beep", "Known IDs normalize case");
Check(AlertSoundCatalog.Normalize("silent") == "silent", "Silent remains selected");

var gate = new AlertSoundGate();
Check(!gate.TryAccept("silent", TimeSpan.Zero), "Silent never requests audible playback");
Check(!gate.TryAccept("silent", TimeSpan.Zero, preview: true), "Silent preview never requests playback");
Check(gate.TryAccept("chime", TimeSpan.Zero, preview: true), "A preview is permitted immediately");
Check(gate.TryAccept("chime", TimeSpan.Zero), "Preview does not delay a real alert");
Check(!gate.TryAccept("bell", TimeSpan.Zero), "A simultaneous arrival is throttled across sound choices");
Check(!gate.TryAccept("bell", TimeSpan.FromMilliseconds(1999)), "A burst inside two seconds is throttled");
Check(gate.TryAccept("bell", TimeSpan.FromSeconds(1), preview: true), "Explicit preview bypasses burst throttling");
Check(gate.TryAccept("chime", TimeSpan.FromSeconds(2)), "A real alert at the exact two-second boundary is accepted");
Check(!gate.TryAccept("chime", TimeSpan.FromMilliseconds(3999)), "Rejected arrivals do not move the cooldown boundary");
Check(gate.TryAccept("urgent", TimeSpan.FromSeconds(4)), "A later real alert is accepted");
var zeroGap = new AlertSoundGate(TimeSpan.Zero);
Check(zeroGap.TryAccept("bell", TimeSpan.Zero) && zeroGap.TryAccept("bell", TimeSpan.Zero), "Explicit zero cooldown permits simultaneous playback requests");
bool negativeRejected = false;
try { _ = new AlertSoundGate(TimeSpan.FromTicks(-1)); }
catch (ArgumentOutOfRangeException) { negativeRejected = true; }
Check(negativeRejected, "Negative cooldown configuration is rejected");

var hashes = new HashSet<string>();
foreach (var choice in AlertSoundCatalog.Choices)
{
    if (choice.Id == "silent") continue;
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Faraz.OutlookAlert.Sounds." + choice.Id + ".wav")
        ?? throw new Exception("Missing embedded sound: " + choice.Id);
    var reader = new BinaryReader(stream);
    string ReadAscii(int count) => System.Text.Encoding.ASCII.GetString(reader.ReadBytes(count));
    Check(ReadAscii(4) == "RIFF", choice.Label + " has a RIFF container");
    uint riffLength = reader.ReadUInt32();
    Check(riffLength + 8 == stream.Length && ReadAscii(4) == "WAVE", choice.Label + " has a complete WAVE container");
    Check(ReadAscii(4) == "fmt " && reader.ReadUInt32() == 16, choice.Label + " has the standard PCM format header");
    ushort encoding = reader.ReadUInt16(), channels = reader.ReadUInt16();
    uint sampleRate = reader.ReadUInt32(), bytesPerSecond = reader.ReadUInt32();
    ushort blockAlign = reader.ReadUInt16(), bits = reader.ReadUInt16();
    Check(encoding == 1 && channels == 1 && sampleRate == 22050 && bits == 16 && blockAlign == 2 && bytesPerSecond == 44100,
        choice.Label + " uses supported mono PCM samples");
    Check(ReadAscii(4) == "data", choice.Label + " contains audio data");
    uint dataLength = reader.ReadUInt32();
    Check(dataLength == stream.Length - stream.Position && dataLength % 2 == 0, choice.Label + " has a complete sample block");
    double duration = dataLength / (double)bytesPerSecond;
    Check(duration >= .5 && duration <= 1.5, choice.Label + " is finite and at most 1.5 seconds");
    var data = reader.ReadBytes(checked((int)dataLength));
    int peak = 0;
    bool endsInSilence = true;
    for (int index = 0; index < data.Length; index += 2)
    {
        short sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(index, 2));
        peak = Math.Max(peak, Math.Abs((int)sample));
        if (index >= data.Length - 200 && sample != 0) endsInSilence = false;
    }
    Check(peak >= 5000 && peak < short.MaxValue, choice.Label + " is audible without clipping");
    Check(endsInSilence, choice.Label + " ends in silence to avoid an abrupt cutoff");
    Check(hashes.Add(Convert.ToHexString(SHA256.HashData(data))), choice.Label + " has distinct sample data");
}
Console.WriteLine($"{passed} sound behavior and PCM checks passed.");
