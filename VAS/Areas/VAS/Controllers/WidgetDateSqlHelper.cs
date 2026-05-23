using System;
using System.Globalization;
using VAdvantage.DataBase;

namespace VIS.Controllers
{
    /// <summary>
    /// Builds Oracle-safe date SQL for dashboard widgets.
    /// Avoids ORA-01830 when day-only format masks receive date-time strings.
    /// </summary>
    internal static class WidgetDateSqlHelper
    {
        internal static string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;

            if (DB.IsOracle())
            {
                return "TO_DATE('"
                    + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "','YYYY-MM-DD')";
            }

            return DB.TO_DATE(day, true);
        }

        internal static string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return columnExpression;
        }

        internal static string AllocationToInvoiceDayDiffSql()
        {
            if (DB.IsOracle())
            {
                return "(TRUNC(AllocationHdr.DateAcct) - TRUNC(Invoice.DateInvoiced))";
            }

            return "(CAST(AllocationHdr.DateAcct AS DATE) - CAST(Invoice.DateInvoiced AS DATE))";
        }
    }
}
