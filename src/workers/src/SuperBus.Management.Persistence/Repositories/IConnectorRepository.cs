using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using SuperBus.Management.Persistence.Entities;

namespace SuperBus.Management.Persistence.Repositories;

public interface IConnectorRepository
{
    Task<Either<Error, ConnectorEntity>> CreateOrReplace(ConnectorEntity member, CancellationToken cancellationToken = default);
    public Task<Either<Error, Option<ConnectorEntity>>> GetById(
        string tenantId,
        string id,
        CancellationToken cancellationToken = default);
}
