using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace AmhsSim.Protocol;

public static class FrameReader
{
    /// <summary>
    /// Reads a single frame from the pipe. Returns null if the pipe is completed and no more frames are available.
    /// </summary>
    public static async IAsyncEnumerable<Frame> ReadFramesAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reader = PipeReader.Create(stream);
        try
        {
                while (true)
                {
                    ReadResult result = await reader.ReadAsync(ct);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (FrameCodec.TryRead(ref buffer, out var frame))
                    {
                        if (frame is not null)
                            yield return frame;
                    }

                    // consumed = what we used; examined = everything, so the next read waits for the new bytes
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    if (result.IsCompleted) yield break;
                }
            }
                finally
            {
                await reader.CompleteAsync();
            }
        }
    }
    
