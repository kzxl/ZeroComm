using System;

namespace ZeroComm.Core.Omron
{
    /// <summary>
    /// Omron PLC memory area codes for FINS protocol frames.
    /// </summary>
    public enum FinsMemoryArea : byte
    {
        CIO_Bit = 0x30,
        WR_Bit = 0x31,
        HR_Bit = 0x32,
        DM_Bit = 0x02,
        CIO_Word = 0xB0,
        WR_Word = 0xB1,
        HR_Word = 0xB2,
        DM_Word = 0x82
    }

    /// <summary>
    /// High-performance builder and parser for Omron FINS (Factory Interface Network Service) communication frames.
    /// Supports FINS UDP and FINS TCP payload wrapping without any external dependencies.
    /// </summary>
    public static class FinsFrame
    {
        public const ushort MemoryAreaReadCommand = 0x0101;
        public const ushort MemoryAreaWriteCommand = 0x0102;

        /// <summary>
        /// Builds a FINS frame for Memory Area Read (Word units).
        /// </summary>
        public static byte[] BuildMemoryAreaReadRequest(
            FinsMemoryArea memoryArea,
            ushort address,
            ushort numberOfItems,
            byte destNode = 0x01,
            byte srcNode = 0xEF,
            byte serviceId = 0x01)
        {
            byte[] frame = new byte[18];

            // FINS Header (10 bytes)
            frame[0] = 0x80; // ICF: Command requiring response
            frame[1] = 0x00; // RSV
            frame[2] = 0x02; // GCT: Max gateway count
            frame[3] = 0x00; // DNA: Dest Network
            frame[4] = destNode; // DA1: Dest Node
            frame[5] = 0x00; // DA2: Dest Unit (CPU)
            frame[6] = 0x00; // SNA: Src Network
            frame[7] = srcNode;  // SA1: Src Node
            frame[8] = 0x00; // SA2: Src Unit
            frame[9] = serviceId; // SID

            // Command: 0x0101 (Memory Area Read)
            frame[10] = 0x01; // MRC
            frame[11] = 0x01; // SRC

            // Parameters
            frame[12] = (byte)memoryArea;
            frame[13] = (byte)(address >> 8);   // Big-endian address
            frame[14] = (byte)(address & 0xFF);
            frame[15] = 0x00;                   // Sub-address (bit = 0)
            frame[16] = (byte)(numberOfItems >> 8);
            frame[17] = (byte)(numberOfItems & 0xFF);

            return frame;
        }

        /// <summary>
        /// Builds a FINS frame for Memory Area Write (Word units).
        /// </summary>
        public static byte[] BuildMemoryAreaWriteRequest(
            FinsMemoryArea memoryArea,
            ushort address,
            ushort[] values,
            byte destNode = 0x01,
            byte srcNode = 0xEF,
            byte serviceId = 0x01)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("Values cannot be empty.", nameof(values));

            ushort numberOfItems = (ushort)values.Length;
            int frameLength = 18 + numberOfItems * 2;
            byte[] frame = new byte[frameLength];

            // FINS Header
            frame[0] = 0x80;
            frame[1] = 0x00;
            frame[2] = 0x02;
            frame[3] = 0x00;
            frame[4] = destNode;
            frame[5] = 0x00;
            frame[6] = 0x00;
            frame[7] = srcNode;
            frame[8] = 0x00;
            frame[9] = serviceId;

            // Command: 0x0102 (Memory Area Write)
            frame[10] = 0x01;
            frame[11] = 0x02;

            // Parameters
            frame[12] = (byte)memoryArea;
            frame[13] = (byte)(address >> 8);
            frame[14] = (byte)(address & 0xFF);
            frame[15] = 0x00;
            frame[16] = (byte)(numberOfItems >> 8);
            frame[17] = (byte)(numberOfItems & 0xFF);

            // Data words (big-endian)
            for (int i = 0; i < values.Length; i++)
            {
                frame[18 + i * 2] = (byte)(values[i] >> 8);
                frame[19 + i * 2] = (byte)(values[i] & 0xFF);
            }

            return frame;
        }

        /// <summary>
        /// Parses a FINS response frame. Returns true if valid and EndCode == 0x0000.
        /// </summary>
        public static bool ParseResponse(
            byte[] frame,
            int offset,
            int count,
            out byte serviceId,
            out ushort endCode,
            out byte[] data)
        {
            serviceId = 0;
            endCode = 0xFFFF;
            data = Array.Empty<byte>();

            // Minimum response: Header 10B + Command 2B + EndCode 2B = 14 bytes
            if (frame == null || count < 14 || offset + count > frame.Length)
                return false;

            serviceId = frame[offset + 9];
            byte mrc = frame[offset + 10];
            byte src = frame[offset + 11];

            // EndCode: Main response code & Sub response code
            byte mainEndCode = frame[offset + 12];
            byte subEndCode = frame[offset + 13];
            endCode = (ushort)((mainEndCode << 8) | subEndCode);

            int dataLength = count - 14;
            if (dataLength > 0)
            {
                data = new byte[dataLength];
                Array.Copy(frame, offset + 14, data, 0, dataLength);
            }

            return endCode == 0x0000;
        }

        /// <summary>
        /// Converts raw big-endian data bytes from a Word response into an array of ushort.
        /// </summary>
        public static ushort[] ToWords(byte[] data)
        {
            if (data == null || data.Length < 2) return Array.Empty<ushort>();
            int wordCount = data.Length / 2;
            ushort[] words = new ushort[wordCount];
            for (int i = 0; i < wordCount; i++)
            {
                words[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
            }
            return words;
        }
    }
}
