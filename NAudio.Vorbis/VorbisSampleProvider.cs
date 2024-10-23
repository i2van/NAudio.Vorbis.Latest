using System;
using System.Collections.Generic;
using System.IO;

using NAudio.Wave;

using NVorbis;
using NVorbis.Contracts;
using NVorbis.Ogg;

using IContainerReader = NVorbis.Contracts.IContainerReader;
using IPacketProvider = NVorbis.Contracts.IPacketProvider;

namespace NAudio.Vorbis;

/// <summary>
/// Implements <see cref="ISampleProvider"/> for NVorbis.
/// </summary>
public sealed class VorbisSampleProvider : ISampleProvider, IDisposable
{
    private readonly LinkedList<IStreamDecoder> _streamDecoders = [];

    private IContainerReader? _containerReader;
    private IStreamDecoder? _streamDecoder;

    private bool _hasEnded;

    /// <summary>
    /// Creates a new instance of <see cref="VorbisSampleProvider"/>.
    /// </summary>
    /// <param name="sourceStream">The stream to read data from.</param>
    /// <param name="closeOnDispose">Close stream on dispose.</param>
    public VorbisSampleProvider(Stream sourceStream, bool closeOnDispose = false)
    {
        _containerReader = new ContainerReader(sourceStream, closeOnDispose)
        {
            NewStreamCallback = ProcessNewStream
        };
        CanSeek = _containerReader.CanSeek;
        if (!_containerReader.TryInit()) throw new ArgumentException("Could not initialize container!");

        if (!GetNextDecoder(true).HasValue)
        {
            throw new InvalidOperationException("Container initialized, but no stream found?");
        }
    }

    /// <summary>
    /// Creates a new instance of <see cref="VorbisSampleProvider"/> from the packet provider.
    /// </summary>
    /// <param name="packetProvider">The packet provider.</param>
    public VorbisSampleProvider(IPacketProvider packetProvider)
        : this(new StreamDecoder(packetProvider), packetProvider.CanSeek)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="VorbisSampleProvider"/> from the decoder stream.
    /// </summary>
    /// <param name="streamDecoder">The decoder stream.</param>
    /// <param name="allowSeek">Sets whether to allow seek operations to be attempted on this stream.
    /// Note that if set to <see langword="true"/> when the underlying stream does not support it
    /// will still generate an exception from the decoder when attempting a seek operations.
    /// </param>
    public VorbisSampleProvider(IStreamDecoder streamDecoder, bool allowSeek)
    {
        _streamDecoders.AddLast(streamDecoder);
        SwitchToDecoder(streamDecoder);
        CanSeek = allowSeek;
    }

    /// <summary>
    /// Gets the number of streams.
    /// </summary>
    public int StreamCount => _streamDecoders.Count;

    /// <summary>
    /// Gets the <see cref="WaveFormat"/> of the current stream.
    /// </summary>
    public WaveFormat? WaveFormat { get; private set; }

    /// <summary>
    /// Gets the position of the current stream in samples.
    /// </summary>
    public long SamplePosition => (_streamDecoder?.SamplePosition).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets whether the current stream is seekable.
    /// </summary>
    public bool CanSeek { get; }

    /// <summary>
    /// Gets the length of the current stream in samples.
    /// </summary>
    public long Length => (_streamDecoder?.TotalSamples).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the stream stats for the current stream.
    /// </summary>
    public IStreamStats Stats => (_streamDecoder?.Stats).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the tag data for the current stream.
    /// </summary>
    public ITagData Tags => (_streamDecoder?.Tags).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the encoder upper bitrate of the current selected Vorbis stream.
    /// </summary>
    public int UpperBitrate => (_streamDecoder?.UpperBitrate).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the encoder nominal bitrate of the current selected Vorbis stream.
    /// </summary>
    public int NominalBitrate => (_streamDecoder?.NominalBitrate).GetValueOrThrowIfNull();

    /// <summary>
    /// Gets the encoder lower bitrate of the current selected Vorbis stream.
    /// </summary>
    public int LowerBitrate => (_streamDecoder?.LowerBitrate).GetValueOrThrowIfNull();

    /// <summary>
    /// Raised when the current stream has been fully read.
    /// </summary>
    public event EventHandler<EndOfStreamEventArgs>? EndOfStream;

    /// <summary>
    /// Raised when a new stream is selected that has a different <see cref="WaveFormat"/> than the previous one.
    /// </summary>
    public event EventHandler? WaveFormatChange;

    /// <summary>
    /// Raised when a new stream is selected.
    /// </summary>
    public event EventHandler? StreamChange;

    /// <summary>
    /// Reads decoded audio data from the current stream.
    /// </summary>
    /// <param name="buffer">The buffer to write the data to.</param>
    /// <param name="offset">The offset into <paramref name="buffer"/> to start writing data.</param>
    /// <param name="count">The number of values to write. This must be a multiple of <see cref="WaveFormat.Channels"/>.</param>
    /// <returns>The number of values read to the <paramref name="buffer"/>.</returns>
    public int Read(float[] buffer, int offset, int count)
    {
        if (_streamDecoder?.IsEndOfStream == true)
        {
            if (_hasEnded)
            {
                // we've ended and don't have any data, so just bail
                return default;
            }

            EndOfStreamEventArgs eosea = new();
            EndOfStream?.Invoke(this, eosea);
            _hasEnded = true;
            if (eosea.AdvanceToNextStream)
            {
                bool? formatChanged = GetNextDecoder(eosea.KeepStream);
                if (formatChanged ?? true)
                {
                    return default;
                }
            }
        }

        return _streamDecoder?.Read(buffer, offset, count) ?? default;
    }

    /// <summary>
    /// Seeks the current stream to the sample position specified.
    /// </summary>
    /// <param name="samplePosition">The sample position to seek to.</param>
    /// <returns>The sample position sought to.</returns>
    public long Seek(long samplePosition)
    {
        if (!CanSeek) throw new InvalidOperationException("Cannot seek underlying stream!");
        if (samplePosition < 0 || samplePosition > _streamDecoder?.TotalSamples) throw new ArgumentOutOfRangeException(nameof(samplePosition));

        _streamDecoder?.SeekTo(samplePosition);

        return _streamDecoder?.SamplePosition ?? default;
    }

    /// <summary>
    /// Removes the stream at the index specified from the internal list and cleans up its resources.
    /// </summary>
    /// <param name="index">The stream index to remove.</param>
    public void RemoveStream(int index) => FindStreamNode(index, ForgetStreamAction);

    /// <summary>
    /// Switches to the stream with index specified.
    /// </summary>
    /// <param name="index">The stream index to switch to.</param>
    /// <returns><see langword="true"/> if the newly selected decoder has a different sample rate or number of channels than the previous one, otherwise <see langword="false"/>.</returns>
    public bool SwitchStreams(int index) => FindStreamNode(index, SwitchStreamsAction);

    /// <summary>
    /// Finds all available streams in the container.
    /// </summary>
    public void FindAllStreams()
    {
        if (!CanSeek)
        {
            if (_containerReader is null) throw new InvalidOperationException("No container loaded!");
            throw new InvalidOperationException("Cannot seek container!  Will discover streams as they are encountered.");
        }

        while (_containerReader?.FindNextStream() == true)
        {
            // Have no idea.
        }
    }

    /// <summary>
    /// Finds the next available stream in the container.
    /// </summary>
    /// <returns><see langword="true"/> if next available Vorbis stream was found, otherwise <see langword="false"/>.</returns>
    public bool FindNextStream()
    {
        if (_containerReader?.CanSeek ?? false)
        {
            IStreamDecoder? lastStream = _streamDecoders.Last?.Value;
            while (_containerReader.FindNextStream() && lastStream == _streamDecoders.Last?.Value)
            {
                // Have no idea.
            }

            return _streamDecoders.Last is not null && lastStream != _streamDecoders.Last.Value;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        bool foundCurrent = false;
        foreach (IStreamDecoder? decoder in _streamDecoders)
        {
            foundCurrent |= decoder == _streamDecoder;
            decoder.Dispose();
        }

        _streamDecoders.Clear();

        if (!foundCurrent)
        {
            _streamDecoder?.Dispose();
        }

        _containerReader?.Dispose();
        _containerReader = null;
    }

    private bool ProcessNewStream(IPacketProvider packetProvider)
    {
        IStreamDecoder decoder;
        try
        {
            decoder = new StreamDecoder(packetProvider);
        }
        catch (Exception ex)
        {
            // an exception here probably means the packet provider returned non-Vorbis data, so warn and reject the stream
            System.Diagnostics.Trace.TraceWarning($"Could not load stream {packetProvider.StreamSerial} due to error: {ex.Message}");

            return false;
        }

        _streamDecoders.AddLast(decoder);

        return true;
    }

    private bool? GetNextDecoder(bool keepOldDecoder)
    {
        // look for the next unplayed decoder after our current decoder
        LinkedListNode<IStreamDecoder>? node;
        if (_streamDecoder is null)
        {
            // first stream...
            node = _streamDecoders.First;
        }
        else
        {
            node = _streamDecoders.Find(_streamDecoder);
            while (node is not null && node.Value.IsEndOfStream)
            {
                node = node.Next;
            }
        }

        // clean up and remove the old decoder if we're not keeping it
        if (!keepOldDecoder && _streamDecoder is not null)
        {
            _streamDecoders.Remove(_streamDecoder);
            _streamDecoder.Dispose();
        }

        // finally, if we still don't have a valid decoder, try to find a new stream in the container
        if (node is null && FindNextStream())
        {
            node = _streamDecoders.Last;
        }

        // switch to the new decoder, if one was found
        return node is not null ? SwitchToDecoder(node.Value) : null;
    }

    private bool SwitchToDecoder(IStreamDecoder nextDecoder)
    {
        _streamDecoder = nextDecoder;
        _hasEnded = false;

        int channels = WaveFormat?.Channels ?? default;
        int sampleRate = WaveFormat?.SampleRate ?? default;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_streamDecoder.SampleRate, _streamDecoder.Channels);

        if (channels != WaveFormat.Channels || sampleRate != WaveFormat.SampleRate)
        {
            WaveFormatChange?.Invoke(this, EventArgs.Empty);

            return true;
        }

        StreamChange?.Invoke(this, EventArgs.Empty);

        return false;
    }

    private delegate T NodeFoundAction<out T>(LinkedListNode<IStreamDecoder> node);

    private T? FindStreamNode<T>(int index, NodeFoundAction<T> action)
    {
        if (_containerReader is null) throw new InvalidOperationException("Cannot operate on more than the current stream if not loaded from stream!");
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        LinkedListNode<IStreamDecoder>? node = _streamDecoders.First;
        int count = -1;
        while (++count < index)
        {
            if (node?.Next is null && !FindNextStream())
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            node = node?.Next;
        }

        return node is not null ? action(node) : default;
    }

    private bool ForgetStreamAction(LinkedListNode<IStreamDecoder> node)
    {
        node.Value.Dispose();
        _streamDecoders.Remove(node);

        return false;
    }

    private bool SwitchStreamsAction(LinkedListNode<IStreamDecoder> node)
    {
        return SwitchToDecoder(node.Value);
    }

    internal int GetCurrentStreamIndex()
    {
        int cnt = -1;
        LinkedListNode<IStreamDecoder>? node = _streamDecoders.First;
        while (node is not null)
        {
            ++cnt;
            if (node.Value == _streamDecoder)
            {
                break;
            }
            node = node.Next;
        }

        return cnt;
    }

    internal int? GetNextStreamIndex()
    {
        if (_containerReader is null) return null;

        int cnt = -1;
        LinkedListNode<IStreamDecoder>? node = _streamDecoders.First;

        while (node is not null)
        {
            ++cnt;
            IStreamDecoder? sd = node.Value;
            node = node.Next;
            if (sd == _streamDecoder)
            {
                break;
            }
        }

        if (node is not null)
        {
            // if we have a node, that means we have at least one known stream after the current one
            return cnt + 1;
        }

        // if we get here, we're out of known streams...

        // if we don't need the current stream or the container can seek, we can try for more...
        if (_containerReader.CanSeek || _streamDecoder?.IsEndOfStream == true)
        {
            ++cnt;
            while (_containerReader.FindNextStream())
            {
                if (_streamDecoders.Count > cnt)
                {
                    return cnt;
                }
            }
        }

        // no more streams
        return null;
    }
}