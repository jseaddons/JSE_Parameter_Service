using System;
using System.Xml.Serialization;

namespace JSE_Parameter_Service.Models
{
    /// <summary>
    /// Simple serializable key-value pair for XML persistence
    /// </summary>
    public class SerializableKeyValue
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}



