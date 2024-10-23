# NAudio.Vorbis.Latest

[![Latest build](https://github.com/i2van/NAudio.Vorbis.Latest/workflows/build/badge.svg)](https://github.com/i2van/NAudio.Vorbis.Latest/actions)
[![NuGet](https://img.shields.io/nuget/v/NAudio.Vorbis.Latest)](https://www.nuget.org/packages/NAudio.Vorbis.Latest)
[![Downloads](https://img.shields.io/nuget/dt/NAudio.Vorbis.Latest)](https://www.nuget.org/packages/NAudio.Vorbis.Latest)
[![License](https://img.shields.io/badge/license-MIT-yellow)](https://opensource.org/licenses/MIT)

[NAudio.Vorbis.Latest](https://github.com/i2van/NAudio.Vorbis.Latest) is a convenience wrapper to enable easy integration of [NVorbis](https://github.com/NVorbis/NVorbis) into [NAudio](https://github.com/naudio/NAudio) projects. [Drop-in replacement](https://en.wikipedia.org/wiki/Drop-in_replacement) for [NAudio.Vorbis](https://www.nuget.org/packages/NAudio.Vorbis) NuGet package.

## Example

```csharp
using NAudio.Vorbis;
using NAudio.Wave;

using var vorbisWaveReader = new VorbisWaveReader("path/to/file.ogg");
using var waveOutEvent = new WaveOutEvent();

waveOutEvent.Init(vorbisWaveReader);
waveOutEvent.Play();

// Wait here until playback stops or should stop.
```
