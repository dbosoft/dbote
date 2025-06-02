using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Management.Persistence;

public static class TableNameSanitizer
{
    public static string Sanitize(string tableName) =>
        tableName.Where(char.IsLetterOrDigit).Aggregate("", (a, c) => a + c);
}
