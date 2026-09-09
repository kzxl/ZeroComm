using System;
using ZeroComm.Core.Modbus;

namespace ZeroComm.Core.Buffers
{
    /// <summary>
    /// Fast zero-copy streaming frame extractor operating directly on CircularRingBuffer.
    /// Solves TCP stream fragmentation and packet coalescing without garbage collection pressure.
    /// </summary>
    public static class StreamingFrameParser
    {
        /// <summary>
        /// Attempts to extract a complete Modbus TCP frame from the ring buffer.
        /// Inspects the MBAP Length field to determine exact packet boundary.
        /// </summary>
        public static bool TryExtractModbusTcpFrame(CircularRingBuffer ring, out byte[] frame)
        {
            frame = Array.Empty<byte>();
            if (ring == null || ring.Count < ModbusTcpFrame.MbapHeaderLength)
                return false;

            // Header: TransactionId(2B) + ProtocolId(2B) + Length(2B) + UnitId(1B)
            byte lenHigh = ring.PeekByte(4);
            byte lenLow = ring.PeekByte(5);
            ushort lengthField = (ushort)((lenHigh << 8) | lenLow);

            // Total frame size = MBAP Header(6 bytes before length payload) + LengthField
            int totalExpectedBytes = 6 + lengthField;

            if (ring.Count < totalExpectedBytes)
                return false; // Still waiting for more stream data

            frame = new byte[totalExpectedBytes];
            ring.Read(frame, 0, totalExpectedBytes);
            return true;
        }

        /// <summary>
        /// Attempts to extract a complete Mitsubishi MELSEC MC Protocol 3E Binary response frame.
        /// Inspects the subheader (0xD0, 0x00) and 2-byte response data length field (offset 7).
        /// </summary>
        public static bool TryExtractMcProtocolFrame(CircularRingBuffer ring, out byte[] frame)
        {
            frame = Array.Empty<byte>();
            if (ring == null || ring.Count < 9)
                return false;

            // Validate subheader (0xD0, 0x00)
            if (ring.PeekByte(0) != 0xD0 || ring.PeekByte(1) != 0x00)
                return false;

            byte lenLow = ring.PeekByte(7);
            byte lenHigh = ring.PeekByte(8);
            ushort dataLength = (ushort)(lenLow | (lenHigh << 8));
            int totalExpectedBytes = 9 + dataLength;

            if (ring.Count < totalExpectedBytes)
                return false;

            frame = new byte[totalExpectedBytes];
            ring.Read(frame, 0, totalExpectedBytes);
            return true;
        }

        /// <summary>
        /// Attempts to extract a frame of exact fixed length.
        /// </summary>
        public static bool TryExtractFixedLengthFrame(CircularRingBuffer ring, int frameLength, out byte[] frame)
        {
            frame = Array.Empty<byte>();
            if (ring == null || ring.Count < frameLength)
                return false;

            frame = new byte[frameLength];
            ring.Read(frame, 0, frameLength);
            return true;
        }

        /// <summary>
        /// Attempts to extract a delimiter-terminated frame (e.g. "\r\n" or STX/ETX).
        /// Delimiter is stripped or retained based on keepDelimiter flag.
        /// </summary>
        public static bool TryExtractDelimitedFrame(
            CircularRingBuffer ring,
            byte[] delimiter,
            out byte[] frame,
            bool keepDelimiter = false)
        {
            frame = Array.Empty<byte>();
            if (ring == null || delimiter == null || delimiter.Length == 0)
                return false;

            int index = ring.IndexOfSequence(delimiter);
            if (index < 0)
                return false;

            int payloadLen = keepDelimiter ? (index + delimiter.Length) : index;
            frame = new byte[payloadLen];
            ring.Read(frame, 0, payloadLen);

            if (!keepDelimiter)
            {
                // Discard delimiter bytes
                ring.Advance(delimiter.Length);
            }

            return true;
        }
    }
}
