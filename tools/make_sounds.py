"""Generate SonarShift's original, mellow plucks using only Python's standard library."""
from pathlib import Path
import math
import struct
import wave

root = Path(__file__).resolve().parent.parent / "Assets"
root.mkdir(exist_ok=True)
rate = 48000
duration = 0.230
count = round(rate * duration)


def pluck(t, frequency):
    if t < 0:
        return 0.0
    # Rounded attack and a fast-decaying upper partial keep the cue soft, not beepy.
    attack = 1 - math.exp(-t / 0.004)
    fundamental = math.sin(2 * math.pi * frequency * t) * math.exp(-t / 0.052)
    warmth = 0.18 * math.sin(2 * math.pi * frequency * 2.003 * t) * math.exp(-t / 0.024)
    return attack * (fundamental + warmth)


for filename, frequencies in [
    ("SwitchOutput.wav", (523.251, 783.991)),
    ("SwitchInput.wav", (587.330, 440.000)),
]:
    floating = []
    for i in range(count):
        t = i / rate
        tone = pluck(t, frequencies[0]) + 0.62 * pluck(t - 0.038, frequencies[1])
        # Fade completely to zero so interrupting or repeating the cue stays unobtrusive.
        tail = min(1.0, max(0.0, (duration - t) / 0.055)) ** 2
        floating.append(tone * tail)
    gain = 0.085 / max(abs(value) for value in floating)
    samples = [round(32767 * gain * value) for value in floating]
    with wave.open(str(root / filename), "wb") as output:
        output.setparams((1, 2, rate, 0, "NONE", "not compressed"))
        output.writeframes(struct.pack(f"<{len(samples)}h", *samples))
    print(f"{filename}: {len(samples) / rate:.3f}s; peak -21.4 dBFS")
