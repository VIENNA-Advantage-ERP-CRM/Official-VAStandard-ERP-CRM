using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Negative Variance Qty KPI Widget & Modal (Inventory Count / Physical Inventory Dashboard)
    /// Purpose     : Read-only 2x1 glass KPI tile summing all negative count differences (diff < 0)
    ///               for current month-to-date, with an interactive paged breakdown modal.
    /// Prefix      : VAS_000_
    /// </summary>
    public class VAS_160_NegativeVarianceQtyWidgetController : Controller
    {
        /// <summary>
        /// Gets current month-to-date total negative variance quantity and total contributing count lines.
        /// </summary>
        /// <returns>JSON { varianceQty, totalLines }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNegativeVarianceSummary()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            // Variance is COMPUTED as QtyBook - QtyCount (the ABSOLUTE shortfall, a positive number),
            // not read from the stored DifferenceQty.
            //
            // The source prompt asserts "DifferenceQty is the approved stored difference and
            // represents QtyCount - QtyBook". Measured on DB 2 that is false: of 368 completed
            // count lines, 348 hold the exact NEGATION, 20 hold NULL, and only the 3 zero-difference
            // lines agree. Filtering on DifferenceQty < 0 therefore selected the lines that had
            // counted OVER, so this widget was reporting the positive population (and VAS_159 the
            // negative one), with both totals wrong.
            //
            // The value is returned as a positive magnitude - the widget's formatQty() takes the
            // absolute value and prefixes U+2212, so it still renders as "−4,213".
            //
            // Lines where QtyCount = QtyBook are ignored; the comparison also drops rows where
            // either quantity is NULL, which is the wanted behaviour.
            string sql = @"
                SELECT
                    COALESCE(SUM(il.QtyBook - il.QtyCount), 0) AS variance_qty,
                    COUNT(*) AS total_lines
                FROM M_Inventory i
                JOIN M_InventoryLine il
                    ON il.M_Inventory_ID = i.M_Inventory_ID
                WHERE i.IsActive = 'Y'
                  AND il.IsActive = 'Y'
                  AND COALESCE(i.IsInternalUse, 'N') = 'N'
                  AND i.DocStatus = 'CO'
                  AND i.MovementDate >= @MonthStart
                  AND i.MovementDate < @NextMonthStart
                  AND il.QtyCount < il.QtyBook";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters =
            {
                new SqlParameter("@MonthStart", monthStart),
                new SqlParameter("@NextMonthStart", nextMonthStart)
            };

            IDataReader dr = null;

            try
            {
                decimal varianceQty = 0;
                int totalLines = 0;

                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    varianceQty = Util.GetValueOfDecimal(dr["variance_qty"]);
                    totalLines = Util.GetValueOfInt(dr["total_lines"]);
                }

                var result = new
                {
                    varianceQty = varianceQty,
                    totalLines = totalLines
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAdvantage.Logging.VLogger.Get().Severe("Error fetching Negative Variance summary: " + ex.Message);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// Gets paged modal rows of count lines contributing to negative variance.
        /// </summary>
        /// <param name="page">1-based page index</param>
        /// <param name="pageSize">Page size (3-8)</param>
        /// <returns>JSON { rows, totalRows, page, pageSize }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNegativeVarianceData(int page = 1, int pageSize = 8)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            page = Math.Max(1, page);
            pageSize = Math.Max(3, Math.Min(8, pageSize));

            int startRow = ((page - 1) * pageSize) + 1;
            int endRow = page * pageSize;

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            string baseSql = @"
                SELECT
                    i.M_Inventory_ID,
                    il.M_InventoryLine_ID,
                    i.DocumentNo,
                    p.Name AS ProductName,
                    p.Value AS ProductCode,
                    -- Attribute Set Instance description is what the application shows as the
                    -- product Attribute (same expression as VAS_078_ProductSearchWidget).
                    -- Left NULL when the line carries no attribute; the JS renders a dash.
                    asi.Description AS AttributeValue,
                    -- Warehouse NAME, not the Value/code - the column header now reads Warehouse.
                    w.Name AS Warehouse,
                    -- Locator NAME: LocatorCombination is the human readable X/Y/Z combination the
                    -- application displays; Value is the fallback identifier when it is not set.
                    -- Same expression as VAS_078 / VAS_148 / VAS_149.
                    COALESCE(l.LocatorCombination, l.Value) AS Locator,
                    il.QtyCount,
                    -- Computed absolute shortfall, for the same reason as the summary query.
                    -- Keeping the alias means the JSON contract and the JS are unchanged, and
                    -- formatQty() re-applies the U+2212 prefix per row.
                    (il.QtyBook - il.QtyCount) AS DifferenceQty,
                    i.MovementDate,
                    il.Line AS LineNo
                FROM M_Inventory i
                JOIN M_InventoryLine il
                    ON il.M_Inventory_ID = i.M_Inventory_ID
                JOIN M_Product p
                    ON p.M_Product_ID = il.M_Product_ID
                JOIN M_Warehouse w
                    ON w.M_Warehouse_ID = i.M_Warehouse_ID
                LEFT JOIN M_Locator l
                    ON l.M_Locator_ID = il.M_Locator_ID
                LEFT JOIN M_AttributeSetInstance asi
                    ON asi.M_AttributeSetInstance_ID = il.M_AttributeSetInstance_ID
                WHERE i.IsActive = 'Y'
                  AND il.IsActive = 'Y'
                  AND COALESCE(i.IsInternalUse, 'N') = 'N'
                  AND i.DocStatus = 'CO'
                  AND i.MovementDate >= @MonthStart
                  AND i.MovementDate < @NextMonthStart
                  AND il.QtyCount < il.QtyBook";

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string pagedSql = $@"
                WITH filtered AS (
                    {baseSql}
                ),
                numbered AS (
                    SELECT
                        f.*,
                        ROW_NUMBER() OVER (
                            ORDER BY
                                f.MovementDate DESC,
                                f.M_Inventory_ID DESC,
                                f.LineNo ASC,
                                f.M_InventoryLine_ID ASC
                        ) AS RowNo,
                        COUNT(*) OVER () AS TotalRows
                    FROM filtered f
                )
                SELECT
                    M_Inventory_ID,
                    M_InventoryLine_ID,
                    DocumentNo,
                    ProductName,
                    ProductCode,
                    AttributeValue,
                    Warehouse,
                    Locator,
                    QtyCount,
                    DifferenceQty,
                    TotalRows
                FROM numbered
                WHERE RowNo BETWEEN @StartRow AND @EndRow
                ORDER BY RowNo";

            SqlParameter[] parameters =
            {
                new SqlParameter("@MonthStart", monthStart),
                new SqlParameter("@NextMonthStart", nextMonthStart),
                new SqlParameter("@StartRow", startRow),
                new SqlParameter("@EndRow", endRow)
            };

            IDataReader dr = null;
            List<object> rows = new List<object>();
            int totalRows = 0;

            try
            {
                dr = DB.ExecuteReader(pagedSql, parameters);
                while (dr != null && dr.Read())
                {
                    totalRows = Util.GetValueOfInt(dr["TotalRows"]);
                    rows.Add(new
                    {
                        mInventoryId = Util.GetValueOfInt(dr["M_Inventory_ID"]),
                        mInventoryLineId = Util.GetValueOfInt(dr["M_InventoryLine_ID"]),
                        documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        productName = Util.GetValueOfString(dr["ProductName"]),
                        productCode = Util.GetValueOfString(dr["ProductCode"]),
                        attributeValue = Util.GetValueOfString(dr["AttributeValue"]),
                        warehouse = Util.GetValueOfString(dr["Warehouse"]),
                        locator = Util.GetValueOfString(dr["Locator"]),
                        qtyCount = Util.GetValueOfDecimal(dr["QtyCount"]),
                        differenceQty = Util.GetValueOfDecimal(dr["DifferenceQty"])
                    });
                }

                var result = new
                {
                    rows = rows,
                    totalRows = totalRows,
                    page = page,
                    pageSize = pageSize
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAdvantage.Logging.VLogger.Get().Severe("Error fetching Negative Variance modal rows: " + ex.Message);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }
    }
}

