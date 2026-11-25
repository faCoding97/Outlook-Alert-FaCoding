"""Reproduce Outlook Alert's original, finite PCM notification clips (stdlib only)."""
from pathlib import Path
import math
import struct
import wave

RATE = 22050
DESTINATION = Path(__file__).resolve().parent


def tone(samples, start, duration, frequency, amplitude, decay=5.0, bell=False):
    first = round(start * RATE)
    count = round(duration * RATE)
    for index in range(count):
        t = index / RATE
        attack = min(1.0, t / 0.012)
        release = min(1.0, (duration - t) / 0.035)
        envelope = attack * release * math.exp(-decay * t / duration)
        value = math.sin(2 * math.pi * frequency * t)
        if bell:
            value = (value + 0.30 * math.sin(2 * math.pi * frequency * 2.76 * t)
                     + 0.12 * math.sin(2 * math.pi * frequency * 5.4 * t)) / 1.42
        samples[first + index] += amplitude * envelope * value


def make(name, duration, notes):
    samples = [0.0] * round(RATE * duration)
    for note in notes:
        tone(samples, **note)
    assert max(abs(value) for value in samples) < 0.95, "clipped source"
    raw = b"".join(struct.pack("<h", round(value * 32767)) for value in samples)
    with wave.open(str(DESTINATION / (name + ".wav")), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(raw)


make("chime", 1.15, [
    dict(start=0.0, duration=0.65, frequency=659.25, amplitude=0.38),
    dict(start=0.17, duration=0.85, frequency=987.77, amplitude=0.40),
])
make("bell", 1.35, [
    dict(start=0.0, duration=1.25, frequency=783.99, amplitude=0.62, decay=4.0, bell=True),
])
make("double-beep", 0.72, [
    dict(start=0.0, duration=0.22, frequency=880.0, amplitude=0.38, decay=0.4),
    dict(start=0.34, duration=0.22, frequency=880.0, amplitude=0.38, decay=0.4),
])
make("soft-ping", 0.90, [
    dict(start=0.0, duration=0.8, frequency=523.25, amplitude=0.34, decay=4.8),
    dict(start=0.0, duration=0.6, frequency=1046.5, amplitude=0.08, decay=5.5),
])
make("urgent", 1.25, [
    dict(start=start, duration=0.18, frequency=frequency, amplitude=0.46, decay=0.5)
    for start, frequency in [(0.0, 783.99), (0.23, 1046.5), (0.58, 783.99), (0.81, 1046.5)]
])

if __name__ == "__main__":
    for path in sorted(DESTINATION.glob("*.wav")):
        with wave.open(str(path), "rb") as clip:
            frames = clip.readframes(clip.getnframes())
            values = struct.unpack("<" + "h" * (len(frames) // 2), frames)
            assert clip.getnchannels() == 1 and clip.getsampwidth() == 2
            assert clip.getframerate() == RATE
            assert 0.5 <= clip.getnframes() / RATE <= 1.5
            assert 0 < max(abs(value) for value in values) < 32767
            assert all(value == 0 for value in values[-100:]), "clip should end in silence"
            print(f"{path.name}: {clip.getnframes() / RATE:.2f}s, peak {max(abs(v) for v in values) / 32767:.3f}")
