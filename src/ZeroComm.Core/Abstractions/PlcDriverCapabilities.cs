namespace ZeroComm.Core.Abstractions
{
    /// <summary>
    /// Describes protocol-specific operational limits and capabilities for a PLC driver.
    /// </summary>
    public sealed class PlcDriverCapabilities
    {
        public PlcVendor Vendor { get; set; }
        public int MaxPduBytes { get; set; } = 240;
        public int MaxBatchReadItems { get; set; } = 20;
        public int MaxContiguousReadBytes { get; set; } = 1000;
        public bool SupportsBitAddressing { get; set; } = true;
        public bool SupportsRandomBatchRead { get; set; } = false;
        public bool SupportsTagNames { get; set; } = false;
    }
}
