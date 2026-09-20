using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZeroComm.Core.Buffers;
using ZeroComm.Core.Transport;

namespace ZeroComm.Core.Siemens
{
    /// <summary>
    /// High-performance asynchronous Siemens S7 PLC communication client (ISO-on-TCP / S7comm).
    /// Supports S7-300, S7-400, S7-1200, and S7-1500 PLCs.
    /// Operates over any <see cref="ITransport"/> (e.g. TcpTransport).
    /// </summary>
    public class S7TcpClient : IDisposable
    {
        private readonly ITransport _transport;
        private readonly CircularRingBuffer _ringBuffer = new CircularRingBuffer(65536);
        private readonly object _ringLock = new object();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<byte[]>? _currentPendingResponse;
        private ushort _sequenceNumber = 1;
        private bool _isDisposed;

        /// <summary>
        /// Gets the underlying transport instance.
        /// </summary>
        public ITransport Transport => _transport;

        /// <summary>
        /// Gets the negotiated maximum S7 PDU length in bytes.
        /// </summary>
        public ushort NegotiatedPduLength { get; private set; } = S7Frame.DefaultPduLength;

        /// <summary>
        /// Gets or sets the default timeout in milliseconds (default: 3000ms).
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// Initializes a new instance of the <see cref="S7TcpClient"/> class.
        /// </summary>
        public S7TcpClient(ITransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.DataReceived += OnDataReceived;
            _transport.OnDisconnected += OnDisconnected;
        }

        private void OnDataReceived(byte[] buffer, int offset, int count)
        {
            lock (_ringLock)
            {
                _ringBuffer.Write(buffer, offset, count);

                while (StreamingFrameParser.TryExtractS7Frame(_ringBuffer, out byte[] frame))
                {
                    var tcs = _currentPendingResponse;
                    if (tcs != null)
                    {
                        _currentPendingResponse = null;
                        tcs.TrySetResult(frame);
                    }
                }
            }
        }

        private void OnDisconnected()
        {
            var tcs = _currentPendingResponse;
            if (tcs != null)
            {
                _currentPendingResponse = null;
                tcs.TrySetException(new IOException("Transport disconnected while awaiting Siemens S7 response."));
            }
        }

        #region Connection & Handshake

        /// <summary>
        /// Performs complete ISO-on-TCP COTP handshake and S7 PDU negotiation.
        /// For S7-1200/1500: rack = 0, slot = 1. For S7-300: rack = 0, slot = 2.
        /// </summary>
        public async Task ConnectHandshakeAsync(int rack = 0, int slot = 1, CancellationToken ct = default)
        {
            // 1. Step 1: Send COTP Connection Request (CR)
            byte[] crPacket = S7Frame.BuildConnectionRequest(rack, slot);
            byte[] ccResponse = await SendAndReceiveAsync(crPacket, DefaultTimeoutMs, ct).ConfigureAwait(false);

            if (!S7Frame.ValidateConnectionConfirm(ccResponse))
            {
                throw new S7Exception("COTP Connection Request was rejected by Siemens PLC.");
            }

            // 2. Step 2: Negotiate S7 Communication Setup PDU
            ushort seq = GetNextSequenceNumber();
            byte[] setupPacket = S7Frame.BuildSetupCommunication(seq, S7Frame.DefaultPduLength);
            byte[] setupResponse = await SendAndReceiveAsync(setupPacket, DefaultTimeoutMs, ct).ConfigureAwait(false);

            NegotiatedPduLength = S7Frame.ParseSetupCommunicationResponse(setupResponse);
        }

        #endregion

        #region Read Operations

        /// <summary>
        /// Reads a block of raw bytes from a specified S7 memory area (e.g. DB, Merkers, Inputs, Outputs).
        /// </summary>
        public async Task<byte[]> ReadBytesAsync(
            S7Area area,
            ushort dbNumber,
            int startByte,
            ushort count,
            CancellationToken ct = default)
        {
            var variable = new S7VariableAddress(area, dbNumber, startByte, count, isBit: false);
            ushort seq = GetNextSequenceNumber();
            byte[] request = S7Frame.BuildReadRequest(seq, new[] { variable });

            byte[] response = await SendAndReceiveAsync(request, DefaultTimeoutMs, ct).ConfigureAwait(false);
            var results = S7Frame.ParseReadResponse(response, expectedItems: 1);
            return results[0];
        }

        /// <summary>
        /// Reads a single bit from a specified S7 memory area.
        /// </summary>
        public async Task<bool> ReadBitAsync(
            S7Area area,
            ushort dbNumber,
            int startByte,
            int bitIndex,
            CancellationToken ct = default)
        {
            if (bitIndex < 0 || bitIndex > 7)
                throw new ArgumentOutOfRangeException(nameof(bitIndex), "Bit index must be between 0 and 7.");

            var variable = new S7VariableAddress(area, dbNumber, startByte, 1, isBit: true, bitIndex: bitIndex);
            ushort seq = GetNextSequenceNumber();
            byte[] request = S7Frame.BuildReadRequest(seq, new[] { variable });

            byte[] response = await SendAndReceiveAsync(request, DefaultTimeoutMs, ct).ConfigureAwait(false);
            var results = S7Frame.ParseReadResponse(response, expectedItems: 1);

            return results[0].Length > 0 && results[0][0] != 0;
        }

        /// <summary>
        /// Reads multiple disparate variables in a single multi-variable PDU request.
        /// </summary>
        public async Task<List<byte[]>> ReadMultiVariablesAsync(
            IReadOnlyList<S7VariableAddress> variables,
            CancellationToken ct = default)
        {
            ushort seq = GetNextSequenceNumber();
            byte[] request = S7Frame.BuildReadRequest(seq, variables);

            byte[] response = await SendAndReceiveAsync(request, DefaultTimeoutMs, ct).ConfigureAwait(false);
            return S7Frame.ParseReadResponse(response, expectedItems: variables.Count);
        }

        #endregion

        #region Write Operations

        /// <summary>
        /// Writes raw bytes to a specified S7 memory area.
        /// </summary>
        public async Task WriteBytesAsync(
            S7Area area,
            ushort dbNumber,
            int startByte,
            byte[] data,
            CancellationToken ct = default)
        {
            var variable = new S7VariableAddress(area, dbNumber, startByte, (ushort)data.Length, isBit: false);
            ushort seq = GetNextSequenceNumber();
            byte[] request = S7Frame.BuildWriteRequest(seq, variable, data);

            byte[] response = await SendAndReceiveAsync(request, DefaultTimeoutMs, ct).ConfigureAwait(false);
            S7Frame.ParseWriteResponse(response);
        }

        /// <summary>
        /// Writes a single bit to a specified S7 memory area.
        /// </summary>
        public async Task WriteBitAsync(
            S7Area area,
            ushort dbNumber,
            int startByte,
            int bitIndex,
            bool value,
            CancellationToken ct = default)
        {
            if (bitIndex < 0 || bitIndex > 7)
                throw new ArgumentOutOfRangeException(nameof(bitIndex), "Bit index must be between 0 and 7.");

            var variable = new S7VariableAddress(area, dbNumber, startByte, 1, isBit: true, bitIndex: bitIndex);
            byte[] data = new byte[] { (byte)(value ? 1 : 0) };

            ushort seq = GetNextSequenceNumber();
            byte[] request = S7Frame.BuildWriteRequest(seq, variable, data);

            byte[] response = await SendAndReceiveAsync(request, DefaultTimeoutMs, ct).ConfigureAwait(false);
            S7Frame.ParseWriteResponse(response);
        }

        #endregion

        #region Typed Helpers (Big-Endian S7 Data Formats)

        public async Task<short> ReadInt16Async(S7Area area, ushort dbNumber, int startByte, CancellationToken ct = default)
        {
            byte[] bytes = await ReadBytesAsync(area, dbNumber, startByte, 2, ct).ConfigureAwait(false);
            return (short)((bytes[0] << 8) | bytes[1]);
        }

        public async Task WriteInt16Async(S7Area area, ushort dbNumber, int startByte, short value, CancellationToken ct = default)
        {
            byte[] bytes = new byte[] { (byte)(value >> 8), (byte)(value & 0xFF) };
            await WriteBytesAsync(area, dbNumber, startByte, bytes, ct).ConfigureAwait(false);
        }

        public async Task<int> ReadInt32Async(S7Area area, ushort dbNumber, int startByte, CancellationToken ct = default)
        {
            byte[] bytes = await ReadBytesAsync(area, dbNumber, startByte, 4, ct).ConfigureAwait(false);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }

        public async Task WriteInt32Async(S7Area area, ushort dbNumber, int startByte, int value, CancellationToken ct = default)
        {
            byte[] bytes = new byte[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
            await WriteBytesAsync(area, dbNumber, startByte, bytes, ct).ConfigureAwait(false);
        }

        public async Task<float> ReadFloatAsync(S7Area area, ushort dbNumber, int startByte, CancellationToken ct = default)
        {
            byte[] bytes = await ReadBytesAsync(area, dbNumber, startByte, 4, ct).ConfigureAwait(false);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToSingle(bytes, 0);
        }

        public async Task WriteFloatAsync(S7Area area, ushort dbNumber, int startByte, float value, CancellationToken ct = default)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            await WriteBytesAsync(area, dbNumber, startByte, bytes, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads standard Siemens S7 String format (Byte 0: MaxLength, Byte 1: ActualLength, Bytes 2..N: ASCII payload).
        /// </summary>
        public async Task<string> ReadStringAsync(S7Area area, ushort dbNumber, int startByte, ushort maxLength, CancellationToken ct = default)
        {
            ushort totalToRead = (ushort)(maxLength + 2);
            byte[] bytes = await ReadBytesAsync(area, dbNumber, startByte, totalToRead, ct).ConfigureAwait(false);

            if (bytes.Length < 2) return string.Empty;
            int actualLength = bytes[1];
            if (actualLength > bytes.Length - 2)
            {
                actualLength = bytes.Length - 2;
            }

            return Encoding.ASCII.GetString(bytes, 2, actualLength);
        }

        #endregion

        #region Internal Dispatch Loop

        private async Task<byte[]> SendAndReceiveAsync(byte[] request, int timeoutMs, CancellationToken ct)
        {
            ThrowIfDisposed();

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _currentPendingResponse = tcs;

                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                linkedCts.Token.Register(() => tcs.TrySetCanceled());

                await _transport.SendAsync(request, 0, request.Length, ct).ConfigureAwait(false);

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _currentPendingResponse = null;
                _sendLock.Release();
            }
        }

        private ushort GetNextSequenceNumber()
        {
            return unchecked(++_sequenceNumber);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(S7TcpClient));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _transport.DataReceived -= OnDataReceived;
            _transport.OnDisconnected -= OnDisconnected;
            _sendLock.Dispose();
        }

        #endregion
    }
}
