using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Locator Transfer Matrix Widget (Material Transfer dashboard)
    /// Purpose     : Returns completed / closed transfer lines for a month/year, aggregated
    ///               as from-locator x to-locator (quantity and document count). Locators
    ///               are labelled with their name COALESCE(LocatorCombination, Bin, Value), never the bare code.
    /// ID Prefix   : VAS_177_
    /// </summary>
    public class VAS_177_LocatorTransferMatrixWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLocatorMatrix(int month, int year)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // month = 0 means the whole year
            DateTime from = new DateTime(year, month <= 0 ? 1 : month, 1);
            DateTime to = month <= 0 ? from.AddYears(1) : from.AddMonths(1);

            // Locator display name = COALESCE(LocatorCombination, Bin, Value); Value is the code and is the last resort.
            string sql = @"
                SELECT lSrc.M_Locator_ID AS FromID,
                       lSrc.LocatorCombination AS FromComb, lSrc.Bin AS FromBin, lSrc.Value AS FromValue,
                       wSrc.Name AS FromWarehouse,
                       lDst.M_Locator_ID AS ToID,
                       lDst.LocatorCombination AS ToComb, lDst.Bin AS ToBin, lDst.Value AS ToValue,
                       wDst.Name AS ToWarehouse,
                       ml.MovementQty AS Qty,
                       mm.M_Movement_ID AS MovementID
                FROM M_MovementLine ml
                INNER JOIN M_Movement mm ON mm.M_Movement_ID = ml.M_Movement_ID
                INNER JOIN M_Locator lSrc ON lSrc.M_Locator_ID = ml.M_Locator_ID
                INNER JOIN M_Warehouse wSrc ON wSrc.M_Warehouse_ID = lSrc.M_Warehouse_ID
                INNER JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                INNER JOIN M_Warehouse wDst ON wDst.M_Warehouse_ID = lDst.M_Warehouse_ID
                WHERE mm.IsActive = 'Y'
                  AND mm.DocStatus IN ('CO', 'CL')
                  AND mm.MovementDate >= " + DB.TO_DATE(from, true) + @"
                  AND mm.MovementDate < " + DB.TO_DATE(to, true);

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "mm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var cells = new Dictionary<string, Cell>();
            var names = new Dictionary<int, string[]>();   // locator id -> { name, warehouse }
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    int f = Util.GetValueOfInt(dr["FromID"]);
                    int t = Util.GetValueOfInt(dr["ToID"]);
                    names[f] = new string[] { PickName(dr, "From"), Util.GetValueOfString(dr["FromWarehouse"]) };
                    names[t] = new string[] { PickName(dr, "To"), Util.GetValueOfString(dr["ToWarehouse"]) };
                    string key = f + "|" + t;
                    Cell c;
                    if (!cells.TryGetValue(key, out c))
                    {
                        c = new Cell { FromID = f, ToID = t };
                        cells[key] = c;
                    }
                    c.Qty += Util.GetValueOfDecimal(dr["Qty"]);
                    c.Docs.Add(Util.GetValueOfInt(dr["MovementID"]));
                }

                // Different locators can share a name (e.g. "Standard" in several warehouses):
                // add the warehouse to those so the matrix axes stay distinguishable.
                var nameCount = new Dictionary<string, int>();
                foreach (var n in names.Values)
                {
                    int cnt;
                    nameCount.TryGetValue(n[0], out cnt);
                    nameCount[n[0]] = cnt + 1;
                }

                var list = new List<object>();
                foreach (var c in cells.Values)
                {
                    list.Add(new
                    {
                        FromID = c.FromID,
                        FromLocator = DisplayName(names[c.FromID], nameCount),
                        ToID = c.ToID,
                        ToLocator = DisplayName(names[c.ToID], nameCount),
                        Qty = c.Qty,
                        Docs = c.Docs.Count
                    });
                }

                return Json(JsonConvert.SerializeObject(new { cells = list }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        private static string PickName(IDataReader dr, string prefix)
        {
            string comb = (Util.GetValueOfString(dr[prefix + "Comb"]) ?? "").Trim();
            if (comb.Length > 0) { return comb; }
            string bin = (Util.GetValueOfString(dr[prefix + "Bin"]) ?? "").Trim();
            if (bin.Length > 0) { return bin; }
            return Util.GetValueOfString(dr[prefix + "Value"]);
        }

        private static string DisplayName(string[] n, Dictionary<string, int> nameCount)
        {
            return nameCount[n[0]] > 1 && !string.IsNullOrEmpty(n[1]) ? n[0] + " (" + n[1] + ")" : n[0];
        }

        private class Cell
        {
            public int FromID;
            public int ToID;
            public decimal Qty;
            public HashSet<int> Docs = new HashSet<int>();
        }
    }
}
