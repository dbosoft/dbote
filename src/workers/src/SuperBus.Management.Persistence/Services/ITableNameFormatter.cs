using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Management.Persistence.Services;

public interface ITableNameFormatter
{
    public string Format(string tableName);
}
