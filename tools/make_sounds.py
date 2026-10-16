#!/usr/bin/env python3
"""Synthesises AVALiberator's sound effects and tunes into ../src/AVALiberator/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVALiberator", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The fleet's missile leaving, and its blast.
    write("Launch.wav", mix(tone(0.22, 1400, 500, "pulse", release=0.6), tone(0.18, 3000, 1200, "noise", release=0.8, gain=0.4)), 0.24)
    write("Blast.wav", mix(tone(0.5, 1800, 120, "noise", attack=0.002, release=0.6), tone(0.35, 180, 50, "square", release=0.6, gain=0.5)), 0.36)
    # A base destroyed, a citadel struck but still standing.
    write("BaseDown.wav", mix(tone(0.8, 900, 60, "noise", release=0.5), tone(0.7, 220, 40, "saw", release=0.5, gain=0.7), tone(0.3, 880, 220, "square", release=0.6, gain=0.3)), 0.46)
    write("CitadelHit.wav", mix(tone(0.25, 500, 380, "square", release=0.5), tone(0.2, 1200, 600, "noise", release=0.7, gain=0.5)), 0.32)
    # The enemy: a rocket rising, a raider leaving its hangar, a sentinel's pellet.
    write("EnemyLaunch.wav", tone(0.45, 160, 520, "saw", attack=0.02, release=0.5, vibrato=0.04, vib_rate=20), 0.22)
    write("RaiderLaunch.wav", cat(*[tone(0.09, f, f * 1.3, "square", release=0.5) for f in [300, 420, 560, 740]]), 0.24)
    write("SentinelShot.wav", tone(0.12, 1800, 2600, "tri", release=0.7), 0.22)
    # A ship lost, and a new one flying in.
    write("ShipLost.wav", mix(tone(1.0, 1200, 40, "noise", release=0.4), tone(0.9, 400, 30, "saw", release=0.4, gain=0.8)), 0.55)
    write("NewShip.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.36)
    # The planet: its name coming up, breaking apart, and the fanfare for its freedom.
    write("PlanetStart.wav", cat(tone(0.5, 110, 220, "saw", attack=0.1, release=0.4, gain=0.6), *[tone(0.12, hz(n), kind="pulse", release=0.4) for n in ["D5", "A5", "D6"]]), 0.34)
    write("PlanetBreak.wav", mix(tone(2.4, 1500, 30, "noise", attack=0.01, release=0.3), tone(2.2, 90, 25, "square", release=0.3, gain=0.7), tone(1.4, 60, 30, "saw", release=0.3, gain=0.5)), 0.6)
    write("Liberated.wav", cat(*[tone(0.12, hz(n), kind="pulse", release=0.4) for n in ["D5", "F#5", "A5", "D6", "A5", "D6", "F#6"]], tone(0.5, hz("A6"), kind="pulse", release=0.8)), 0.4)
    write("Spin.wav", tone(0.3, 90, 140, "tri", attack=0.05, release=0.6, vibrato=0.08, vib_rate=14), 0.16)

    # The battle: a low, pulsing drone with a slow figure over it. Loops (whole bars at 100 bpm).
    bass = [(n, 1) for n in ["D2", "D2", "A2", "D2", "C2", "C2", "G2", "C2", "Bb1", "Bb1", "F2", "Bb1", "A1", "A1", "E2", "A1"]]
    lead = [("D4", 2), ("F4", 2), ("E4", 2), ("C4", 2), ("D4", 2), ("Bb3", 2), ("A3", 4)]
    write("Music.wav", tune(100, lead, bass, kind="tri"), 0.2)

    # The title: a heroic march in D (an original composition).
    lead = [("D5", 0.5), ("A4", 0.5), ("D5", 0.5), ("F#5", 0.5), ("A5", 1), ("F#5", 0.5), ("A5", 0.5), ("B5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F#5", 0.5), ("E5", 2),
            ("E5", 0.5), ("B4", 0.5), ("E5", 0.5), ("G5", 0.5), ("B5", 1), ("A5", 0.5), ("G5", 0.5), ("F#5", 0.5), ("E5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 2)]
    bass = [("D3", 1), ("A2", 1), ("D3", 1), ("A2", 1), ("G2", 1), ("D3", 1), ("A2", 2),
            ("E3", 1), ("B2", 1), ("E3", 1), ("G2", 1), ("A2", 1), ("A2", 1), ("D3", 2)]
    write("TitleTune.wav", cat(tune(132, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(150, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 0.5), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(70, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D2", 2), ("Bb1", 2), ("A1", 2), ("D2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
