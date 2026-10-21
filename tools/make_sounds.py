"""Generate Sonora's original, quiet UI chimes using only Python's standard library."""
from pathlib import Path
import math
import struct
import wave

root = Path(__file__).resolve().parent.parent / "Assets"
root.mkdir(exist_ok=True)
rate = 48000

for filename, frequencies in [
    ("SwitchOutput.wav", (784.0, 1046.5)),
    ("SwitchInput.wav", (698.5, 523.25)),
]:
    samples = []
    for index, frequency in enumerate(frequencies):
        duration = 0.058 if index == 0 else 0.082
        count = round(rate * duration)
        for i in range(count):
            t = i / rate
            envelope = math.sin(math.pi * i / (count - 1)) ** 2
            tone = math.sin(2 * math.pi * frequency * t)
            tone += 0.12 * math.sin(4 * math.pi * frequency * t)
            samples.append(round(32767 * 0.10 * envelope * tone / 1.12))
        if index == 0:
            samples.extend([0] * round(rate * 0.008))
    with wave.open(str(root / filename), "wb") as output:
        output.setparams((1, 2, rate, 0, "NONE", "not compressed"))
        output.writeframes(struct.pack(f"<{len(samples)}h", *samples))
    print(f"{filename}: {len(samples) / rate:.3f}s")
