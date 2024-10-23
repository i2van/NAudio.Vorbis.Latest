using System;
using System.IO;
using System.Linq;

using NAudio.Wave;
using NVorbis.Contracts;

namespace NAudio.Vorbis;

/// <summary>
/// Implements <see cref="WaveStream"/> for NVorbis.
/// </summary>
public class VorbisWaveReader : WaveStream, ISampleProvider
{
    private VorbisSampleProvider? _sampleProvider;

    /// <summary>
    /// Creates a new instance of <see cref="VorbisWaveReader"/> from the file.
    /// </summary>
    /// <param name="fileName">The <c>.ogg</c> file.</param>
    public VorbisWaveReader(string fileName)
        : this(File.OpenRead(fileName), true)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="VorbisWaveReader"/> from the stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="closeOnDispose">Close the stream on dispose.</param>
    public VorbisWaveReader(Stream stream, bool closeOnDispose = false)
    {
        // To maintain consistent semantics with v1.1, we don't expose the events and auto-advance / stream removal features of VorbisSampleProvider.
        // If one wishes to use those features, they should really use VorbisSampleProvider directly...
        _sampleProvider = new VorbisSampleProvider(stream, closeOnDispose);
    }

    /// <summary>
    /// Gets the <see cref="WaveFormat"/> of the current stream.
    /// </summary>
    public override WaveFormat WaveFormat => (_sampleProvider?.WaveFormat).GetValueOrThrowIfNull();

    /// <inheritdoc />
    public override long Length => (_sampleProvider?.Length).GetValueOrThrowIfNull()  * (_sampleProvider?.WaveFormat?.BlockAlign).GetValueOrThrowIfNull();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException" accessor="set">If the sample provider is not seekable.</exception>
    /// <exception cref="ArgumentOutOfRangeException" accessor="set">If the value is out of bounds.</exception>
    public override long Position
    {
        get => (_sampleProvider?.SamplePosition).GetValueOrThrowIfNull() * (_sampleProvider?.WaveFormat?.BlockAlign).GetValueOrThrowIfNull();
        set
        {
            if (_sampleProvider?.CanSeek == false) throw new InvalidOperationException("Cannot seek!");
            if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(value));

            _sampleProvider?.Seek(value / (_sampleProvider.WaveFormat?.BlockAlign).GetValueOrThrowIfNull());
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        // adjust count so it is in floats instead of bytes
        count /= sizeof(float);

        // make sure we don't have an odd count
        count -= count % (_sampleProvider?.WaveFormat?.Channels).GetValueOrThrowIfNull();

        if (count <= 0)
        {
            count = 1;
        }

        float[] cb = new float[count];

        // let Read(float[], int, int) do the actual reading; adjust count back to bytes
        int cnt = Read(cb, 0, count) * sizeof(float);

        // move the data back to the request buffer
        Buffer.BlockCopy(cb, 0, buffer, offset, cnt);

        // done!
        return cnt;
    }

    /// <inheritdoc />
    public int Read(float[] buffer, int offset, int count)
    {
        return (_sampleProvider?.Read(buffer, offset, count)).GetValueOrThrowIfNull();
    }

    /// <summary>
    /// Gets the number of streams.
    /// </summary>
    public int StreamCount => (_sampleProvider?.StreamCount).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets or sets the next stream index.
    /// </summary>
    public int? NextStreamIndex { get; set; }

    /// <summary>
    /// Advances to the next stream index.
    /// </summary>
    /// <returns><see langword="true"/> if another Vorbis stream was found, otherwise <see langword="false"/>.</returns>
    public bool GetNextStreamIndex()
    {
        if (!NextStreamIndex.HasValue)
        {
            NextStreamIndex = _sampleProvider?.GetNextStreamIndex();
            return NextStreamIndex.HasValue;
        }

        return false;
    }

    /// <summary>
    /// Gets or sets the current stream index.
    /// </summary>
    /// <returns>The current stream index.</returns>
    public int CurrentStream
    {
        get => (_sampleProvider?.GetCurrentStreamIndex()).GetValueOrThrowIfNull();
        set
        {
            _sampleProvider?.SwitchStreams(value);

            NextStreamIndex = null;
        }
    }

    /// <summary>
    /// Gets the encoder upper bitrate of the current selected Vorbis stream.
    /// </summary>
    public int UpperBitrate => (_sampleProvider?.UpperBitrate).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the encoder nominal bitrate of the current selected Vorbis stream.
    /// </summary>
    public int NominalBitrate => (_sampleProvider?.NominalBitrate).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the encoder lower bitrate of the current selected Vorbis stream.
    /// </summary>
    public int LowerBitrate => (_sampleProvider?.LowerBitrate).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the encoder vendor string for the current selected Vorbis stream.
    /// </summary>
    public string Vendor => (_sampleProvider?.Tags.EncoderVendor).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the comments in the current selected Vorbis stream.
    /// </summary>
    public string[] Comments => (_sampleProvider?.Tags.All.SelectMany(static keyValuePair => keyValuePair.Value, static (keyValuePair, Item) => $"{keyValuePair.Key}={Item}").ToArray()).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets stats from each decoder stream available.
    /// </summary>
    public IStreamStats[] Stats => [ (_sampleProvider?.Stats).GetValueOrThrowIfNull() ];

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sampleProvider?.Dispose();
            _sampleProvider = null;
        }

        base.Dispose(disposing);
    }
}