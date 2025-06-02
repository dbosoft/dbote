using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dbosoft.Azure.TableStorage;
using LanguageExt;
using LanguageExt.Common;
using SuperBus.Management.Persistence.Entities;
using SuperBus.Management.Persistence.Services;

namespace SuperBus.Management.Persistence.Repositories;

public class ConnectorRepository(
    ICloudTableProvider tableProvider,
    ITableNameFormatter tableNameFormatter)
    : DefaultDataRepository<ConnectorEntity>(tableProvider), IConnectorRepository
{
    public override string TableName => tableNameFormatter.Format(TableNames.Connectors);

    public Task<Either<Error, ConnectorEntity>> CreateOrReplace(
        ConnectorEntity member,
        CancellationToken cancellationToken = default) =>
        CreateOrReplace(member, ConnectorEntity.PointQuery(member.TenantId, member.Id), cancellationToken);

    public Task<Either<Error, Option<ConnectorEntity>>> GetById(
        string tenantId,
        string id,
        CancellationToken cancellationToken = default) =>
        GetByEntityKey<ConnectorEntity>(ConnectorEntity.PointQuery(tenantId, id), cancellationToken: cancellationToken);
}
