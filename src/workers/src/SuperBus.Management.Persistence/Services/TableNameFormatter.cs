using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Management.Persistence.Services;

public class TableNameFormatter(string prefix) : ITableNameFormatter
{
    public string Format(string tableName) => Sanitize($"{prefix}-{tableName}");

    private static string Sanitize(string tableName) =>
        new(tableName.Where(char.IsLetterOrDigit).ToArray());
}
