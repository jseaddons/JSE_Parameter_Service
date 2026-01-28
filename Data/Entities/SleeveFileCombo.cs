using System;

namespace JSE_Parameter_Service.Data.Entities
{
    /// <summary>
    /// SQLite entity for FileCombos table
    /// Represents a unique combination of LinkedFile and HostFile
    /// </summary>
    public class SleeveFileCombo
    {
        public int ComboId { get; set; }
        public int FilterId { get; set; }
        public string LinkedFileKey { get; set; } = string.Empty;
        public string HostFileKey { get; set; } = string.Empty;
        public DateTime? ProcessedAt { get; set; }
    }
}

