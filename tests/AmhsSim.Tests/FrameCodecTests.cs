using System.Buffers;
using AmhsSim.Protocol;

namespace AmhsSim.Tests;

public class FrameCodecTests
{
    private static readonly Frame Sample = new(MessageType.MoveTo, 7, Payloads.MoveTo(65_000));

    [Fact]
    public void Encode_then_decode_gives_the_same_frame()
    {
        var buffer = new ReadOnlySequence<byte>(FrameCodec.Encode(Sample));

        Assert.True(FrameCodec.TryRead(ref buffer, out var frame));
        Assert.Equal(Sample.Type, frame.Type);
        Assert.Equal(Sample.TxId, frame.TxId);
        Assert.Equal(Sample.Payload, frame.Payload);
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void Half_a_frame_waits_for_more_bytes()
    {
        byte[] bytes = FrameCodec.Encode(Sample);
        var buffer = new ReadOnlySequence<byte>(bytes[..4]);

        Assert.False(FrameCodec.TryRead(ref buffer, out _));
        Assert.Equal(4, buffer.Length); // nothing thrown away
    }

    [Fact]
    public void Two_frames_in_one_read_are_both_found()
    {
        byte[] both = [.. FrameCodec.Encode(Sample), .. FrameCodec.Encode(Sample with { TxId = 8 })];
        var buffer = new ReadOnlySequence<byte>(both);

        Assert.True(FrameCodec.TryRead(ref buffer, out var first));
        Assert.True(FrameCodec.TryRead(ref buffer, out var second));
        Assert.Equal(7, first.TxId);
        Assert.Equal(8, second.TxId);
    }

    [Fact]
    public void Garbage_and_a_corrupt_frame_are_skipped()
    {
        byte[] corrupt = FrameCodec.Encode(Sample);
        corrupt[^1] ^= 0xFF;
        byte[] stream = [0xAA, 0xBB, .. corrupt, .. FrameCodec.Encode(Sample with { TxId = 9 })];
        var buffer = new ReadOnlySequence<byte>(stream);

        Assert.True(FrameCodec.TryRead(ref buffer, out var frame));
        Assert.Equal(9, frame.TxId);
    }

    [Fact]
    public void Status_payload_round_trips()
    {
        var status = new VehicleStatus(VehicleState.Acquiring, SubStep.Grip, 65_000, 1_500, true, true, AlarmCode.None);
        Assert.Equal(status, VehicleStatus.FromPayload(status.ToPayload()));
    }

    [Fact]
    public async Task Reader_handles_a_stream_that_delivers_one_byte_at_a_time()
    {
        byte[] bytes = [.. FrameCodec.Encode(Sample), .. FrameCodec.Encode(Sample with { TxId = 8 })];
        var frames = new List<Frame>();

        await foreach (var f in FrameReader.ReadFramesAsync(new TrickleStream(bytes)))
            frames.Add(f);

        Assert.Equal([7, 8], frames.Select(f => (int)f.TxId));
    }

    /// <summary>A stream that returns at most one byte per read, like a slow network.</summary>
    private sealed class TrickleStream(byte[] data) : Stream
    {
        private int _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= data.Length || count == 0) return 0;
            buffer[offset] = data[_pos++];
            return 1;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
