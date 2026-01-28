using JSE_Parameter_Service.Models;
using JSE_Parameter_Service.Data.Entities;

namespace JSE_Parameter_Service.Services.Strategies
{
    public interface IPrefixStrategy
    {
        /// <summary>
        /// Determines the prefix for a specific ClashZone based on category, settings, and element parameters (System Type).
        /// </summary>
        string ResolvePrefix(string category, MarkPrefixSettings settings, ClashZone zone);
    }
}
