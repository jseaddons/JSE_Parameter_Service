using System.Collections.Generic;
using JSE_Parameter_Service.Data.Repositories;

namespace JSE_Parameter_Service.Services.Interface
{
    public interface IParameterSnapshotTransferService
    {
        void TransferSnapshots(IEnumerable<int> sleeveIds, IClashZoneRepository repository);
    }
}
